using System.Text.Json;
using TZTEK.VispecCMM.Import.Core.Extraction;

namespace TZTEK.VispecCMM.Import.Core.Serialization;

internal static class RawDocumentJsonMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static RawDocument FromJson(JsonElement root, string fallbackFilePath)
    {
        var sourceType = ReadEnum(root, "sourceType", ImportSourceType.Drawing2D);
        var filePath = ReadString(root, "filePath") ?? fallbackFilePath;
        var rawElements = new List<RawElement>();

        if (root.TryGetProperty("rawElements", out var rawElementsElement)
            && rawElementsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in rawElementsElement.EnumerateArray())
            {
                rawElements.Add(new RawElement
                {
                    Id = ReadString(element, "id") ?? Guid.NewGuid().ToString("N"),
                    ElementType = ReadString(element, "elementType") ?? "UNKNOWN",
                    GeometryData = CloneObject(element, "geometryData") ?? CloneObject(element, "geometry"),
                    Annotations = ReadStringDictionary(element, "annotations")
                });
            }
        }

        if (root.TryGetProperty("structuredItems", out var structuredItems))
        {
            rawElements.Add(new RawElement
            {
                Id = "structured_items",
                ElementType = "STRUCTURED_ITEMS",
                GeometryData = JsonSerializer.Deserialize<StructuredImportPayload>(
                    structuredItems.GetRawText(),
                    SerializerOptions),
                Annotations = new Dictionary<string, string>
                {
                    ["source"] = "structuredItems"
                }
            });
        }

        return new RawDocument(sourceType, filePath, rawElements);
    }

    public static StructuredImportPayload? ReadStructuredPayload(object? value)
    {
        if (value is StructuredImportPayload payload)
            return payload;

        if (value is JsonElement element)
            return JsonSerializer.Deserialize<StructuredImportPayload>(element.GetRawText(), SerializerOptions);

        if (value is string json && !string.IsNullOrWhiteSpace(json))
            return JsonSerializer.Deserialize<StructuredImportPayload>(json, SerializerOptions);

        return null;
    }

    internal static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement element, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var item in property.EnumerateObject())
            result[item.Name] = item.Value.ValueKind == JsonValueKind.String
                ? item.Value.GetString() ?? string.Empty
                : item.Value.GetRawText();

        return result;
    }

    private static object? CloneObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return JsonSerializer.Deserialize<object>(property.GetRawText(), SerializerOptions);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static T ReadEnum<T>(JsonElement element, string propertyName, T fallback)
        where T : struct
    {
        var value = ReadString(element, propertyName);
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
