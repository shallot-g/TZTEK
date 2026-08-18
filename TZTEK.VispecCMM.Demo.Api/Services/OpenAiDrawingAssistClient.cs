using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TZTEK.VispecCMM.Demo.Api.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

public sealed class OpenAiDrawingAssistClient : IDrawingAssistProvider
{
    private const string PromptVersion = "drawing-assist-v5-gpt-5.6-non-planar-priority";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiDrawingAssistClient> _logger;
    private readonly PdfPageRenderer _renderer = new();

    public OpenAiDrawingAssistClient(HttpClient httpClient, ILogger<OpenAiDrawingAssistClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _logger = logger;
    }

    public string Provider => "openai";

    public DrawingAssistProviderInfo GetInfo()
    {
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        return new DrawingAssistProviderInfo
        {
            Id = Provider,
            DisplayName = "GPT-5.6",
            IsConfigured = configured,
            Model = GetModel(),
            UnavailableReason = configured ? null : "后端未配置 OPENAI_API_KEY"
        };
    }

    public async Task<DrawingAssistModelResult> AnalyzeAsync(
        string pdfPath,
        IReadOnlyList<VisualizationFeatureDto> features,
        Action<DrawingAssistProgressUpdate>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("找不到待识别的 PDF 图纸。", pdfPath);

        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("未配置 OPENAI_API_KEY，请先在后端 PowerShell 环境变量中设置 OpenAI API Key。");

        var model = GetModel();
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
        var reasoningEffort = NormalizeReasoningEffort(Environment.GetEnvironmentVariable("OPENAI_REASONING_EFFORT"));
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("OPENAI_TIMEOUT_SECONDS"), out var configured)
            ? Math.Clamp(configured, 30, 600) : 300;
        var prompt = VolcengineDrawingAssistClient.BuildPrompt(features);
        var pdfHash = await VolcengineDrawingAssistClient.ComputeHashAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        var runId = $"run_{Guid.NewGuid():N}";
        var outputDirectory = Path.Combine(Path.GetDirectoryName(pdfPath)!, "drawing-pages");
        reportProgress?.Invoke(new DrawingAssistProgressUpdate("RenderingDrawing", 10));
        var pages = await _renderer.RenderPagesAsync(pdfPath, outputDirectory, cancellationToken).ConfigureAwait(false);
        reportProgress?.Invoke(new DrawingAssistProgressUpdate("RenderingDrawing", 25));

        var recommendations = new List<AiFeatureRecommendation>();
        var warnings = new List<string>();
        var requestIds = new List<string>();
        var diagnostics = new List<DrawingAssistPageDiagnostic>();
        var targetCount = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new DrawingAssistProgressUpdate("AnalyzingPages", 25 + (int)Math.Round(45d * index / pages.Count)));
            try
            {
                var result = await AnalyzePageAsync(page, prompt, features, key, model, baseUrl, reasoningEffort, timeoutSeconds, runId, pdfHash, cancellationToken).ConfigureAwait(false);
                targetCount += result.TargetCount;
                recommendations.AddRange(result.Recommendations);
                diagnostics.AddRange(result.PageDiagnostics);
                if (!string.IsNullOrWhiteSpace(result.RequestId)) requestIds.Add(result.RequestId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                warnings.Add($"PDF 第 {page.PageNumber} 页识别失败：{ex.Message}");
                if (ex is DrawingAssistPageException pageException) diagnostics.Add(pageException.Diagnostic);
                _logger.LogWarning(ex, "OpenAI page failed page={Page} width={Width} height={Height} pixels={Pixels}", page.PageNumber, page.Width, page.Height, page.PixelCount);
            }
        }

        if (requestIds.Count == 0)
            throw new InvalidOperationException($"识别失败：所有 PDF 页面均未产生可解析结果。{string.Join("；", warnings)}");

        reportProgress?.Invoke(new DrawingAssistProgressUpdate("AnalyzingPages", 70));
        return new DrawingAssistModelResult
        {
            Provider = Provider,
            Model = model,
            TargetCount = targetCount,
            Recommendations = recommendations.GroupBy(item => item.FeatureId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Confidence).First()).ToList(),
            RequestId = string.Join(",", requestIds),
            Warnings = warnings,
            PageDiagnostics = diagnostics
        };
    }

    private async Task<DrawingAssistModelResult> AnalyzePageAsync(
        RenderedDrawingPage page, string prompt, IReadOnlyList<VisualizationFeatureDto> features,
        string key, string model, string baseUrl, string reasoningEffort, int timeoutSeconds,
        string runId, string pdfHash, CancellationToken cancellationToken)
    {
        var imageBytes = await File.ReadAllBytesAsync(page.Path, cancellationToken).ConfigureAwait(false);
        var imageHash = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        var pageRunId = $"{runId}_page_{page.PageNumber}";
        var startedAt = DateTime.UtcNow;
        var request = new
        {
            model,
            reasoning = new { effort = reasoningEffort },
            max_output_tokens = 81920,
            input = new object[]
            {
                new { role = "system", content = new object[] { new { type = "input_text", text = "你是三坐标测量工程师。只从给定候选 FeatureId 中选择，不得创造 ID。只返回严格 JSON，不要 Markdown。" } } },
                new { role = "user", content = new object[]
                {
                    new { type = "input_text", text = $"这是 PDF 第 {page.PageNumber} 页的完整整页图像，不是局部裁剪。\n本次运行标识：{pageRunId}\n{prompt}" },
                    new { type = "input_image", image_url = $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}", detail = "high" }
                } }
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
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            var responseParsed = TryParseResponse(body, out var root);
            var requestId = responseParsed ? ReadString(root, "id") : ReadHeader(response, "x-request-id");
            if (!response.IsSuccessStatusCode)
            {
                var messageText = responseParsed
                    ? FormatError(response.StatusCode, root, requestId)
                    : $"OpenAI 视觉请求失败。HTTP {(int)response.StatusCode}，request ID={requestId ?? "未知"}，Content-Type={contentType}，响应摘要={SafeSnippet(body)}";
                throw new DrawingAssistPageException(messageText, CreateDiagnostic(page, model, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, false, 0, pageRunId, pdfHash, imageHash, startedAt));
            }
            if (!responseParsed)
                throw new DrawingAssistPageException(
                    $"OpenAI 返回了非 JSON/SSE HTTP 响应。HTTP {(int)response.StatusCode}，request ID={requestId ?? "未知"}，Content-Type={contentType}，响应摘要={SafeSnippet(body)}",
                    CreateDiagnostic(page, model, response.StatusCode, default, requestId, stopwatch.ElapsedMilliseconds, false, 0, pageRunId, pdfHash, imageHash, startedAt, "InvalidResponseFormat"));

            var content = VolcengineDrawingAssistClient.ExtractResponseText(root);
            var diagnostic = CreateDiagnostic(page, model, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, false, content.Length, pageRunId, pdfHash, imageHash, startedAt);
            _logger.LogInformation("OpenAI response page={Page} status={Status} responseStatus={ResponseStatus} requestId={RequestId} model={Model} fields={Fields} output={OutputCount} textLength={TextLength} inputTokens={InputTokens} outputTokens={OutputTokens} cachedTokens={CachedTokens} runId={RunId} imageHash={ImageHash}",
                page.PageNumber, (int)response.StatusCode, diagnostic.ResponseStatus, requestId, model, diagnostic.ResponseFields, diagnostic.OutputCount, diagnostic.ResponseTextLength, diagnostic.InputTokens, diagnostic.OutputTokens, diagnostic.CachedTokens, diagnostic.RunId, diagnostic.ImageHash);
            if (string.IsNullOrWhiteSpace(content))
            {
                var reason = HasReasoningOnly(root) ? "OpenAI 只返回了推理内容，没有最终答案。" : "OpenAI 请求成功，但未找到可解析的最终文本字段。";
                throw new DrawingAssistPageException($"{reason} HTTP 200，request ID={requestId ?? "未知"}，status={diagnostic.ResponseStatus}。", diagnostic);
            }

            DrawingAssistModelResult parsed;
            try { parsed = VolcengineDrawingAssistClient.ParseModelResult(content, features, requestId, page.PageNumber); }
            catch (JsonException ex)
            {
                throw new DrawingAssistPageException($"OpenAI 返回了文本，但不是合法推荐 JSON：{ex.Message}；响应摘要={VolcengineDrawingAssistClient.TrimForDiagnostic(content)}", diagnostic);
            }
            return new DrawingAssistModelResult
            {
                Provider = Provider, Model = model, TargetCount = parsed.TargetCount, Recommendations = parsed.Recommendations,
                RequestId = requestId, PageDiagnostics = [CreateDiagnostic(page, model, response.StatusCode, root, requestId, stopwatch.ElapsedMilliseconds, true, content.Length, pageRunId, pdfHash, imageHash, startedAt)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DrawingAssistPageException($"OpenAI 第 {page.PageNumber} 页视觉请求超过 {timeoutSeconds} 秒。运行 ID={pageRunId}。", CreateDiagnostic(page, model, null, default, null, stopwatch.ElapsedMilliseconds, false, 0, pageRunId, pdfHash, imageHash, startedAt, "ClientTimeout"));
        }
    }

    private static string GetModel() => Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.6-sol";
    private static string NormalizeReasoningEffort(string? value) => value?.Trim().ToLowerInvariant() is "none" or "low" or "medium" or "high" or "xhigh" or "max" ? value.Trim().ToLowerInvariant() : "medium";
    private static bool TryParseResponse(string body, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException) { }

        // Some OpenAI-compatible gateways return SSE even when stream is omitted.
        foreach (var line in body.Split('\n').Reverse())
        {
            var value = line.Trim();
            if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var payload = value[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            try
            {
                using var eventDocument = JsonDocument.Parse(payload);
                var eventRoot = eventDocument.RootElement;
                if (eventRoot.ValueKind == JsonValueKind.Object
                    && eventRoot.TryGetProperty("response", out var response)
                    && response.ValueKind == JsonValueKind.Object)
                {
                    root = response.Clone();
                    return true;
                }
                if (eventRoot.ValueKind == JsonValueKind.Object && eventRoot.TryGetProperty("output", out _))
                {
                    root = eventRoot.Clone();
                    return true;
                }
            }
            catch (JsonException) { }
        }

        root = default;
        return false;
    }

    private static string SafeSnippet(string body)
    {
        var normalized = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500] + "...";
    }
    private static string? ReadHeader(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    private static string? ReadString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? ReadLong(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static bool HasReasoningOnly(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.EnumerateArray().Any(item => ReadString(item, "type") == "reasoning");

    private static DrawingAssistPageDiagnostic CreateDiagnostic(RenderedDrawingPage page, string model, HttpStatusCode? status, JsonElement root, string? requestId, long elapsed, bool success, int textLength, string runId, string pdfHash, string imageHash, DateTime startedAt, string? statusOverride = null)
    {
        var isObject = root.ValueKind == JsonValueKind.Object;
        var usage = isObject && root.TryGetProperty("usage", out var value) ? value : default;
        var details = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens_details", out var inputDetails) ? inputDetails : default;
        var outputCount = isObject && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array ? output.GetArrayLength() : 0;
        return new DrawingAssistPageDiagnostic
        {
            Provider = "openai", RunId = runId, PdfHash = pdfHash, ImageHash = imageHash, PromptVersion = PromptVersion,
            PageNumber = page.PageNumber, HttpStatusCode = status is null ? null : (int)status.Value, RequestId = requestId, Model = model,
            ResponseFields = isObject ? string.Join(",", root.EnumerateObject().Select(item => item.Name)) : string.Empty,
            OutputCount = outputCount, ContentKinds = outputCount > 0 ? "output:Array" : string.Empty, ResponseTextLength = textLength,
            ElapsedMilliseconds = elapsed, Success = success, ResponseStatus = ReadString(root, "status") ?? statusOverride ?? string.Empty,
            IncompleteDetails = isObject && root.TryGetProperty("incomplete_details", out var incomplete) ? incomplete.ToString() : null,
            InputTokens = ReadLong(usage, "input_tokens"), OutputTokens = ReadLong(usage, "output_tokens"), TotalTokens = ReadLong(usage, "total_tokens"),
            CachedTokens = ReadLong(details, "cached_tokens"), CacheHit = ReadLong(details, "cached_tokens").GetValueOrDefault() > 0,
            StartedAt = startedAt, CompletedAt = DateTime.UtcNow
        };
    }

    private static string FormatError(HttpStatusCode status, JsonElement root, string? requestId)
    {
        var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? ReadString(error, "message") : null;
        var prefix = status switch
        {
            HttpStatusCode.Unauthorized => "OpenAI 认证失败，请检查 OPENAI_API_KEY。",
            (HttpStatusCode)429 => "OpenAI 请求受到限流，请稍后重试或检查账户额度。",
            HttpStatusCode.NotFound => "OpenAI 模型或接口不存在，请检查 OPENAI_MODEL 和 OPENAI_BASE_URL。",
            _ => "OpenAI 视觉请求失败。"
        };
        return $"{prefix} HTTP {(int)status}，request ID={requestId ?? "未知"}{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}")}";
    }
}
