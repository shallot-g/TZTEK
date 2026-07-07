using System.Net.Http.Json;
using System.Text.Json;

namespace TZTEK.VispecCMM.Import.Core.External;

public sealed class LocalLlmClient : ILocalLlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public LocalLlmClient()
        : this(new HttpClient())
    {
    }

    internal LocalLlmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateStructuredJsonAsync(string prompt, CancellationToken ct = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("TZTEK_OLLAMA_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:11434";

        var model = Environment.GetEnvironmentVariable("TZTEK_OLLAMA_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "deepseek-r1:8b";

        var request = new
        {
            model,
            prompt,
            stream = false,
            format = StructuredJsonSchema,
            options = new { temperature = 0 }
        };

        using var response = await _httpClient.PostAsJsonAsync(
                $"{baseUrl.TrimEnd('/')}/api/generate",
                request,
                SerializerOptions,
                ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
                cancellationToken: ct)
            .ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("response", out var responseElement))
            throw new InvalidOperationException("Ollama response does not contain a 'response' field.");

        return responseElement.GetString() ?? string.Empty;
    }

    private static readonly object StructuredJsonSchema = new
    {
        type = "object",
        properties = new
        {
            primitives = new { type = "array" },
            tolerances = new { type = "array" },
            links = new { type = "array" },
            datums = new { type = "array" },
            coordinateSystems = new { type = "array" },
            uncertainItems = new { type = "array" }
        },
        required = new[]
        {
            "primitives",
            "tolerances",
            "links",
            "datums",
            "coordinateSystems",
            "uncertainItems"
        }
    };
}
