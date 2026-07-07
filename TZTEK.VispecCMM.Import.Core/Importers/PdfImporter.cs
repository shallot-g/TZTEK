using System.Text.Json;
using TZTEK.VispecCMM.Import.Core.External;
using TZTEK.VispecCMM.Import.Core.Prompts;
using TZTEK.VispecCMM.Import.Core.Serialization;

namespace TZTEK.VispecCMM.Import.Core.Importers;

public sealed class PdfImporter : FileFormatImporterBase
{
    private readonly ILocalOcrClient _ocrClient;
    private readonly ILocalLlmClient _llmClient;

    public PdfImporter(ILocalOcrClient ocrClient, ILocalLlmClient llmClient)
    {
        _ocrClient = ocrClient;
        _llmClient = llmClient;
    }

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public override async Task<RawDocument> ParseAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        var ocrText = await _ocrClient.RecognizeAsync(filePath, ct).ConfigureAwait(false);
        var prompt = MeasurementDrawingPromptBuilder.BuildFromOcrText(filePath, ocrText);
        var structuredJson = await _llmClient.GenerateStructuredJsonAsync(prompt, ct).ConfigureAwait(false);

        using var structuredDocument = JsonDocument.Parse(structuredJson);
        var rawElements = new List<RawElement>
        {
            new()
            {
                Id = "pdf_ocr_text",
                ElementType = "OCR_TEXT",
                GeometryData = ocrText,
                Annotations = new Dictionary<string, string>
                {
                    ["source"] = "DeepSeek-OCR"
                }
            },
            new()
            {
                Id = "structured_items",
                ElementType = "STRUCTURED_ITEMS",
                GeometryData = RawDocumentJsonMapper.ReadStructuredPayload(structuredDocument.RootElement),
                Annotations = new Dictionary<string, string>
                {
                    ["source"] = "DeepSeek-R1"
                }
            }
        };

        return new RawDocument(ImportSourceType.Drawing2D, filePath, rawElements);
    }
}
