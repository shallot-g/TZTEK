using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using TZTEK.VispecCMM.Demo.Api.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

public sealed class VolcengineDrawingAssistClient : IDrawingAssistClient
{
    private const string PromptVersion = "drawing-assist-v4-no-thinking";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<VolcengineDrawingAssistClient> _logger;
    private readonly PdfPageRenderer _renderer = new();

    public VolcengineDrawingAssistClient(HttpClient httpClient, ILogger<VolcengineDrawingAssistClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _logger = logger;
    }

    public async Task<DrawingAssistModelResult> AnalyzeAsync(
        string pdfPath,
        IReadOnlyList<VisualizationFeatureDto> features,
        Action<DrawingAssistProgressUpdate>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("找不到待识别的 PDF 图纸。", pdfPath);

        var key = Environment.GetEnvironmentVariable("ARK_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("未配置 ARK_API_KEY，请先在后端 PowerShell 环境变量中设置火山方舟 API Key。");

        var endpoint = Environment.GetEnvironmentVariable("ARK_VISION_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("未配置 ARK_VISION_ENDPOINT_ID，请填写火山方舟视觉模型推理接入点 ID。");

        var baseUrl = Environment.GetEnvironmentVariable("ARK_BASE_URL")
            ?? "https://ark.cn-beijing.volces.com/api/v3";
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("ARK_TIMEOUT_SECONDS"), out var configured)
            ? Math.Clamp(configured, 30, 600) : 300;
        var prompt = BuildPrompt(features);
        var pdfHash = await ComputeHashAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        var runId = $"run_{Guid.NewGuid():N}";
        var outputDirectory = Path.Combine(Path.GetDirectoryName(pdfPath)!, "drawing-pages");
        reportProgress?.Invoke(new DrawingAssistProgressUpdate("RenderingDrawing", 10));
        var pages = await _renderer.RenderPagesAsync(pdfPath, outputDirectory, cancellationToken).ConfigureAwait(false);
        reportProgress?.Invoke(new DrawingAssistProgressUpdate("RenderingDrawing", 25));

        var recommendations = new List<AiFeatureRecommendation>();
        var warnings = new List<string>();
        var requestIds = new List<string>();
        var pageDiagnostics = new List<DrawingAssistPageDiagnostic>();
        var targetCount = 0;
        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pages[index];
            reportProgress?.Invoke(new DrawingAssistProgressUpdate(
                "AnalyzingPages",
                25 + (int)Math.Round(45d * index / pages.Count)));
            try
            {
                var result = await AnalyzePageAsync(page, prompt, features, key, endpoint, baseUrl, timeoutSeconds, runId, pdfHash, cancellationToken).ConfigureAwait(false);
                targetCount += result.TargetCount;
                recommendations.AddRange(result.Recommendations);
                pageDiagnostics.AddRange(result.PageDiagnostics);
                if (!string.IsNullOrWhiteSpace(result.RequestId)) requestIds.Add(result.RequestId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var warning = $"PDF 第 {page.PageNumber} 页识别失败：{ex.Message}";
                warnings.Add(warning);
                if (ex is DrawingAssistPageException pageException)
                    pageDiagnostics.Add(pageException.Diagnostic);
                _logger.LogWarning(ex,
                    "Volcengine page failed page={Page} width={Width} height={Height} pixels={Pixels}",
                    page.PageNumber, page.Width, page.Height, page.PixelCount);
            }
        }

        if (requestIds.Count == 0)
            throw new InvalidOperationException($"识别失败：所有 PDF 页面均未产生可解析结果。{string.Join("；", warnings)}");

        reportProgress?.Invoke(new DrawingAssistProgressUpdate("AnalyzingPages", 70));
        var merged = recommendations
            .GroupBy(item => item.FeatureId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Confidence).First())
            .ToList();
        return new DrawingAssistModelResult
        {
            TargetCount = targetCount,
            Recommendations = merged,
            RequestId = string.Join(",", requestIds),
            Model = endpoint,
            Warnings = warnings,
            PageDiagnostics = pageDiagnostics
        };
    }

    private async Task<DrawingAssistModelResult> AnalyzePageAsync(
        RenderedDrawingPage page,
        string prompt,
        IReadOnlyList<VisualizationFeatureDto> features,
        string key,
        string endpoint,
        string baseUrl,
        int timeoutSeconds,
        string runId,
        string pdfHash,
        CancellationToken cancellationToken)
    {
        var imageBytes = await File.ReadAllBytesAsync(page.Path, cancellationToken).ConfigureAwait(false);
        var imageBase64 = Convert.ToBase64String(imageBytes);
        var imageHash = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        var startedAt = DateTime.UtcNow;
        var pageRunId = $"{runId}_page_{page.PageNumber}";
        var request = new
        {
            model = endpoint,
            temperature = 0,
            max_output_tokens = 81920,
            thinking = new { type = "disabled" },
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = "你是三坐标测量工程师。只从给定候选 FeatureId 中选择，不得创造 ID。返回严格 JSON，不要 Markdown。" }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = $"这是 PDF 第 {page.PageNumber} 页的完整整页图像，不是局部裁剪。\n本次运行标识：{pageRunId}\n{prompt}" },
                        new { type = "input_image", image_url = $"data:image/jpeg;base64,{imageBase64}" }
                    }
                }
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(message, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorRoot = TryParseJson(body);
                var errorRequestId = response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
                var errorDiagnostic = CreateDiagnostic(page, endpoint, response.StatusCode, errorRoot, errorRequestId, stopwatch.ElapsedMilliseconds, false, 0, pageRunId, pdfHash, imageHash, startedAt);
                throw new DrawingAssistPageException(FormatErrorMessage(response.StatusCode, body), errorDiagnostic);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var requestId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
            var content = ExtractResponseText(root);
            var diagnostic = CreateDiagnostic(page, endpoint, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, false, content.Length, pageRunId, pdfHash, imageHash, startedAt);
            _logger.LogInformation(
                "Volcengine response page={Page} status={Status} responseStatus={ResponseStatus} requestId={RequestId} fields={Fields} output={OutputCount} choices={ChoicesCount} contentKinds={ContentKinds} textLength={TextLength} inputTokens={InputTokens} outputTokens={OutputTokens} cachedTokens={CachedTokens} cacheHit={CacheHit} runId={RunId} imageHash={ImageHash}",
                page.PageNumber, (int)response.StatusCode, diagnostic.ResponseStatus, requestId, diagnostic.ResponseFields, diagnostic.OutputCount, diagnostic.ChoicesCount, diagnostic.ContentKinds, diagnostic.ResponseTextLength, diagnostic.InputTokens, diagnostic.OutputTokens, diagnostic.CachedTokens, diagnostic.CacheHit, diagnostic.RunId, diagnostic.ImageHash);
            if (string.IsNullOrWhiteSpace(content))
                throw new DrawingAssistPageException(
                    $"豆包请求成功，但未找到可解析的文本字段。HTTP {(int)response.StatusCode}，request ID={requestId ?? "未知"}，响应字段={diagnostic.ResponseFields}，output={diagnostic.OutputCount}，choices={diagnostic.ChoicesCount}，content类型={diagnostic.ContentKinds}。",
                    diagnostic);
            DrawingAssistModelResult result;
            try
            {
                result = ParseModelResult(content, features, requestId, page.PageNumber);
            }
            catch (JsonException ex)
            {
                throw new DrawingAssistPageException(
                    $"豆包返回了文本，但不是合法推荐 JSON：{ex.Message}；响应摘要={TrimForDiagnostic(content)}",
                    CreateDiagnostic(page, endpoint, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, false, content.Length, pageRunId, pdfHash, imageHash, startedAt));
            }
            var successDiagnostic = CreateDiagnostic(page, endpoint, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, true, content.Length, pageRunId, pdfHash, imageHash, startedAt);
            return new DrawingAssistModelResult
            {
                TargetCount = result.TargetCount,
                Recommendations = result.Recommendations,
                RequestId = result.RequestId,
                Model = endpoint,
                PageDiagnostics = [successDiagnostic]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DrawingAssistPageException($"豆包第 {page.PageNumber} 页视觉请求超过 {timeoutSeconds} 秒（客户端超时，不能判断为 max token）。运行 ID={pageRunId}。", CreateDiagnostic(page, endpoint, null, default, null, stopwatch.ElapsedMilliseconds, false, 0, pageRunId, pdfHash, imageHash, startedAt, "ClientTimeout"));
        }
        finally
        {
            _logger.LogInformation(
                "Volcengine drawing page page={Page} width={Width} height={Height} pixels={Pixels} endpoint={Endpoint} elapsed={ElapsedMs}ms",
                page.PageNumber, page.Width, page.Height, page.PixelCount, endpoint, stopwatch.ElapsedMilliseconds);
        }
    }

    private static string BuildPrompt(IReadOnlyList<VisualizationFeatureDto> features)
    {
        var descriptors = features.Select(feature => new
        {
            feature.Id,
            feature.Type,
            feature.Position,
            feature.Direction,
            feature.Radius,
            feature.Length,
            feature.IsInnerSurface
        });
        return "请先观察整张工程图中的主视图、俯视图、侧视图和剖视图，只识别被尺寸、公差、基准或引出线明确指向的对象。\n"
            + "结合箭头、尺寸线、标注位置和视图关系判断目标，再从候选 STEP 工程基元中选择最可能的 FeatureId。\n"
            + "二维圆不能直接证明三维圆柱；无法确认时仍可给出最佳候选，但 RequiresReview=true。\n"
            + "不要输出思考过程，不要解释分析过程，只返回最终 JSON。必须在最终响应中输出 targets 字段。即使无法匹配，也必须返回 {\"targets\":[]}。\n"
            + "只返回 JSON：{\"targets\":[{\"pageNumber\":1,\"annotationId\":\"a1\",\"featureId\":\"候选ID\",\"alternativeFeatureIds\":[],\"confidence\":0.0,\"reason\":\"依据\",\"requiresReview\":false}]}\n"
            + "候选 STEP 基元：\n"
            + JsonSerializer.Serialize(descriptors, JsonOptions);
    }

    private static string ExtractResponseText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText))
            return ReadTextValue(outputText);
        if (root.TryGetProperty("output", out var output))
        {
            var text = ReadKnownContent(output);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        if (root.TryGetProperty("choices", out var choices))
        {
            var text = ReadKnownContent(choices);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    private static string ReadKnownContent(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join("\n", value.EnumerateArray().Select(ReadKnownContent).Where(text => !string.IsNullOrWhiteSpace(text)));
        if (value.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in new[] { "message", "content", "text", "value" })
        {
            if (value.TryGetProperty(property, out var child))
            {
                var text = property is "text" or "value" ? ReadTextValue(child) : ReadKnownContent(child);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return string.Empty;
    }

    private static string ReadTextValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join("\n", value.EnumerateArray().Select(ReadTextValue).Where(text => !string.IsNullOrWhiteSpace(text))),
        JsonValueKind.Object => value.TryGetProperty("text", out var text) ? ReadTextValue(text) : value.TryGetProperty("value", out var nested) ? ReadTextValue(nested) : string.Empty,
        _ => string.Empty
    };

    private static DrawingAssistPageDiagnostic CreateDiagnostic(RenderedDrawingPage page, string model, System.Net.HttpStatusCode? status, JsonElement root, string? requestId, long elapsed, bool success, int textLength, string? runId = null, string? pdfHash = null, string? imageHash = null, DateTime? startedAt = null, string? responseStatusOverride = null)
    {
        var isObject = root.ValueKind == JsonValueKind.Object;
        var fields = isObject ? string.Join(",", root.EnumerateObject().Select(property => property.Name)) : string.Empty;
        var outputCount = isObject && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array ? output.GetArrayLength() : 0;
        var choicesCount = isObject && root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array ? choices.GetArrayLength() : 0;
        var kinds = new List<string>();
        if (isObject && root.TryGetProperty("output", out var outputValue)) kinds.Add($"output:{outputValue.ValueKind}");
        if (isObject && root.TryGetProperty("choices", out var choicesValue)) kinds.Add($"choices:{choicesValue.ValueKind}");
        var responseStatus = ReadString(root, "status") ?? responseStatusOverride ?? (status is null ? "" : status.Value.ToString());
        var usage = isObject && root.TryGetProperty("usage", out var usageValue) ? usageValue : default;
        var input = ReadLong(usage, "input_tokens") ?? ReadLong(usage, "prompt_tokens");
        var outputTokens = ReadLong(usage, "output_tokens") ?? ReadLong(usage, "completion_tokens");
        var total = ReadLong(usage, "total_tokens");
        var cached = ReadLong(usage, "cached_tokens") ?? ReadLong(usage, "cache_read_input_tokens");
        var incomplete = isObject && root.TryGetProperty("incomplete_details", out var incompleteValue) ? incompleteValue.ToString() : null;
        return new DrawingAssistPageDiagnostic
        {
            RunId = runId ?? string.Empty, PdfHash = pdfHash ?? string.Empty, ImageHash = imageHash ?? string.Empty, PromptVersion = PromptVersion,
            PageNumber = page.PageNumber, HttpStatusCode = status is null ? null : (int)status.Value, RequestId = requestId,
            Model = model, ResponseFields = fields, OutputCount = outputCount, ChoicesCount = choicesCount,
            ContentKinds = string.Join(",", kinds), ResponseTextLength = textLength, ElapsedMilliseconds = elapsed, Success = success,
            ResponseStatus = responseStatus, IncompleteDetails = incomplete, InputTokens = input, OutputTokens = outputTokens,
            TotalTokens = total, CachedTokens = cached, CacheHit = cached.GetValueOrDefault() > 0 || (isObject && root.TryGetProperty("caching_store", out _)),
            StartedAt = startedAt ?? DateTime.UtcNow, CompletedAt = DateTime.UtcNow
        };
    }

    private static string? ReadString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? ReadLong(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static async Task<string> ComputeHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static JsonElement TryParseJson(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string FormatErrorMessage(System.Net.HttpStatusCode statusCode, string body)
    {
        var snippet = body[..Math.Min(body.Length, 500)];
        if (body.Contains("only support text messages", StringComparison.OrdinalIgnoreCase))
            return $"当前接入点只支持文本输入，不支持整页图像。请切换到多模态视觉接入点。HTTP {(int)statusCode}：{snippet}";
        return $"豆包整页图像请求失败（HTTP {(int)statusCode}）：{snippet}";
    }

    private static DrawingAssistModelResult ParseModelResult(string content, IReadOnlyList<VisualizationFeatureDto> features, string? requestId, int defaultPageNumber)
    {
        var json = ExtractJsonPayload(content);
        using var document = JsonDocument.Parse(json);
        var allowed = features.Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recommendations = new List<AiFeatureRecommendation>();
        if (document.RootElement.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in targets.EnumerateArray())
            {
                var id = target.TryGetProperty("featureId", out var idElement) ? idElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || !allowed.Contains(id))
                    continue;
                var alternatives = target.TryGetProperty("alternativeFeatureIds", out var alt) && alt.ValueKind == JsonValueKind.Array
                    ? alt.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(allowed.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : [];
                var confidence = target.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDouble(out var value)
                    ? Math.Clamp(value, 0, 1) : 0;
                var review = target.TryGetProperty("requiresReview", out var reviewElement) && reviewElement.ValueKind == JsonValueKind.True;
                recommendations.Add(new AiFeatureRecommendation
                {
                    FeatureId = id,
                    Status = review || confidence < 0.6 || alternatives.Count > 0 ? "NeedsReview" : "Recommended",
                    Confidence = confidence,
                    Reason = target.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty,
                    PageNumber = target.TryGetProperty("pageNumber", out var page) && page.TryGetInt32(out var pageNumber) ? pageNumber : defaultPageNumber,
                    AnnotationId = target.TryGetProperty("annotationId", out var annotation) ? annotation.GetString() : null,
                    AlternativeFeatureIds = alternatives
                });
            }
        }
        return new DrawingAssistModelResult { TargetCount = recommendations.Count, Recommendations = recommendations, RequestId = requestId };
    }

    private static string ExtractJsonPayload(string content)
    {
        var json = content.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = json.IndexOf('\n');
            json = firstLineEnd >= 0 ? json[(firstLineEnd + 1)..] : json;
            var closingFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0) json = json[..closingFence];
            json = json.Trim();
        }

        try
        {
            using var direct = JsonDocument.Parse(json);
            return json;
        }
        catch (JsonException)
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start) return json[start..(end + 1)];
            throw;
        }
    }

    private static string TrimForDiagnostic(string value) => value.Length <= 500 ? value : value[..500] + "...";
}
