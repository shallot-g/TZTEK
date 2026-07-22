using System.Text.Json;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Core.Serialization;

internal static class WorkpieceMeshJsonMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<WorkpieceMesh> ReadMeshes(JsonElement root)
    {
        if (!root.TryGetProperty("workpieceMeshes", out var meshesElement)
            || meshesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var meshes = new List<WorkpieceMesh>();
        foreach (var meshElement in meshesElement.EnumerateArray())
            meshes.Add(ReadMesh(meshElement));

        return meshes;
    }

    public static WorkpieceMesh ReadMesh(JsonElement element)
    {
        return new WorkpieceMesh
        {
            Id = ReadString(element, "id") ?? Guid.NewGuid().ToString("N"),
            Name = ReadString(element, "name") ?? string.Empty,
            SolidIndex = ReadInt(element, "solidIndex"),
            Vertices = ReadDoubleArray(element, "vertices"),
            Triangles = ReadIntArray(element, "triangles"),
            Normals = ReadDoubleArray(element, "normals")
        };
    }

    public static string SerializeMeshes(IReadOnlyList<WorkpieceMesh> meshes)
    {
        var payload = meshes.Select(mesh => new
        {
            mesh.Id,
            mesh.Name,
            mesh.SolidIndex,
            mesh.Vertices,
            mesh.Triangles,
            mesh.Normals
        });

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static IReadOnlyList<WorkpieceMesh> DeserializeMeshes(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        return document.RootElement.EnumerateArray()
            .Select(ReadMesh)
            .ToList();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback = 0)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static IReadOnlyList<double> ReadDoubleArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.GetDouble())
            .ToArray();
    }

    private static IReadOnlyList<int> ReadIntArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.GetInt32())
            .ToArray();
    }
}
