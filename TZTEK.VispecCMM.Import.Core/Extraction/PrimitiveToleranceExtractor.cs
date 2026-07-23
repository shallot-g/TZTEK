using System.Text.Json;
using TZTEK.VispecCMM.Import.Core.Serialization;

namespace TZTEK.VispecCMM.Import.Core.Extraction;

public sealed class PrimitiveToleranceExtractor : IPrimitiveToleranceExtractor
{
    public Task<IReadOnlyList<PrimitiveToleranceItem>> ExtractAsync(
        RawDocument document,
        ImportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var structuredPayloads = document.Elements
            .Where(element => string.Equals(element.ElementType, "STRUCTURED_ITEMS", StringComparison.OrdinalIgnoreCase))
            .Select(element => RawDocumentJsonMapper.ReadStructuredPayload(element.GeometryData))
            .Where(payload => payload is not null)
            .Cast<StructuredImportPayload>()
            .ToList();

        if (structuredPayloads.Count == 0)
            structuredPayloads.Add(BuildPayloadFromRawElements(document.Elements));

        var primitiveById = new Dictionary<string, Primitive>(StringComparer.OrdinalIgnoreCase);
        var tolerancesById = new Dictionary<string, Tolerance>(StringComparer.OrdinalIgnoreCase);
        var links = new List<StructuredLinkDto>();

        foreach (var payload in structuredPayloads)
        {
            foreach (var primitiveDto in payload.Primitives)
            {
                var primitive = CreatePrimitive(primitiveDto);
                if (primitive is null || !PassesPrimitiveFilter(primitive, options))
                    continue;

                primitiveById[primitive.Id] = primitive;
            }

            foreach (var toleranceDto in payload.Tolerances)
            {
                var tolerance = CreateTolerance(toleranceDto);
                if (tolerance is not null)
                    tolerancesById[tolerance.Id] = tolerance;
            }

            links.AddRange(payload.Links);
        }

        var toleranceIdsByPrimitiveId = links
            .Where(link => primitiveById.ContainsKey(link.PrimitiveId))
            .GroupBy(link => link.PrimitiveId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(link => link.ToleranceIds)
                    .Where(tolerancesById.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var items = new List<PrimitiveToleranceItem>();
        foreach (var primitive in primitiveById.Values)
        {
            toleranceIdsByPrimitiveId.TryGetValue(primitive.Id, out var toleranceIds);
            var tolerances = toleranceIds is null
                ? []
                : toleranceIds.Select(id => tolerancesById[id]).ToList();

            if (options.OnlyTolerancedPrimitives && tolerances.Count == 0)
                continue;

            items.Add(new PrimitiveToleranceItem
            {
                Primitive = primitive,
                Tolerances = tolerances
            });
        }

        return Task.FromResult<IReadOnlyList<PrimitiveToleranceItem>>(items);
    }

    private static StructuredImportPayload BuildPayloadFromRawElements(IReadOnlyList<RawElement> elements)
    {
        var payload = new StructuredImportPayload();

        foreach (var element in elements)
        {
            var primitive = CreatePrimitiveFromRawElement(element);
            if (primitive is not null)
                payload.Primitives.Add(primitive);

            foreach (var tolerance in CreateTolerancesFromRawElement(element))
                payload.Tolerances.Add(tolerance);
        }

        var firstPrimitive = payload.Primitives.FirstOrDefault();
        if (firstPrimitive is not null && payload.Tolerances.Count > 0)
        {
            payload.Links.Add(new StructuredLinkDto
            {
                PrimitiveId = firstPrimitive.Id,
                ToleranceIds = payload.Tolerances.Select(tolerance => tolerance.Id).ToList()
            });
        }

        return payload;
    }

    private static StructuredPrimitiveDto? CreatePrimitiveFromRawElement(RawElement element)
    {
        var geometry = ToJsonElement(element.GeometryData);
        var type = NormalizeType(element.ElementType);

        return type switch
        {
            "POINT" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Point",
                SourceElementId = element.Id,
                Point = ReadPoint(geometry, "point") ?? ReadPoint(geometry, "location")
            },
            "LINE" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Line",
                SourceElementId = element.Id,
                Start = ReadPoint(geometry, "start"),
                End = ReadPoint(geometry, "end"),
                Direction = ReadPoint(geometry, "direction")
            },
            "CIRCLE" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Circle",
                SourceElementId = element.Id,
                Center = ReadPoint(geometry, "center"),
                Radius = ReadDouble(geometry, "radius"),
                Normal = ReadPoint(geometry, "normal") ?? [0, 0, 1]
            },
            "ARC" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Arc",
                SourceElementId = element.Id,
                Center = ReadPoint(geometry, "center"),
                Radius = ReadDouble(geometry, "radius"),
                StartAngleRad = ReadDouble(geometry, "startAngleRad"),
                EndAngleRad = ReadDouble(geometry, "endAngleRad"),
                Normal = ReadPoint(geometry, "normal") ?? [0, 0, 1]
            },
            "PLANE" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Plane",
                SourceElementId = element.Id,
                Point = ReadPoint(geometry, "point"),
                Normal = ReadPoint(geometry, "normal") ?? [0, 0, 1],
                Area = ReadDouble(geometry, "area")
            },
            "CYLINDER" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Cylinder",
                SourceElementId = element.Id,
                AxisPoint = ReadPoint(geometry, "axisPoint"),
                AxisDirection = ReadPoint(geometry, "axisDirection") ?? ReadPoint(geometry, "axisDir"),
                AxisStart = ReadPoint(geometry, "axisStart"),
                AxisEnd = ReadPoint(geometry, "axisEnd"),
                AxisCenter = ReadPoint(geometry, "axisCenter"),
                RadialReference = ReadPoint(geometry, "radialReference"),
                Radius = ReadDouble(geometry, "radius"),
                Length = ReadDouble(geometry, "length"),
                StartAngleRad = ReadDouble(geometry, "uMin") ?? ReadDouble(geometry, "startAngleRad"),
                EndAngleRad = ReadDouble(geometry, "uMax") ?? ReadDouble(geometry, "endAngleRad"),
                AngularSpanRad = ReadDouble(geometry, "angularSpanRad"),
                IsInnerSurface = ReadBool(geometry, "isInnerSurface"),
                SurfaceOrientation = ReadString(geometry, "surfaceOrientation"),
                Area = ReadDouble(geometry, "area")
            },
            "SPHERE" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Sphere",
                SourceElementId = element.Id,
                Center = ReadPoint(geometry, "center"),
                Radius = ReadDouble(geometry, "radius")
            },
            "CONE" => new StructuredPrimitiveDto
            {
                Id = element.Id,
                Type = "Cone",
                SourceElementId = element.Id,
                Apex = ReadPoint(geometry, "apex"),
                AxisDirection = ReadPoint(geometry, "axisDirection") ?? ReadPoint(geometry, "axisDir"),
                HalfAngleRad = ReadDouble(geometry, "halfAngleRad")
            },
            _ => null
        };
    }

    private static IEnumerable<StructuredToleranceDto> CreateTolerancesFromRawElement(RawElement element)
    {
        if (!element.Annotations.TryGetValue("text", out var text))
            yield break;

        var normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        var dimensionType = normalized.Contains('Φ') || normalized.Contains('⌀')
            ? "Diameter"
            : normalized.Contains('R')
                ? "Radius"
                : "Linear";

        if (!TryReadFirstNumber(normalized, out var nominal))
            yield break;

        var upper = 0d;
        var lower = 0d;
        var plusMinusIndex = normalized.IndexOf('±');
        if (plusMinusIndex >= 0 && TryReadFirstNumber(normalized[(plusMinusIndex + 1)..], out var deviation))
        {
            upper = deviation;
            lower = -deviation;
        }

        yield return new StructuredToleranceDto
        {
            Id = $"{element.Id}_tol",
            Type = "Dimensional",
            SourceElementId = element.Id,
            DimensionType = dimensionType,
            NominalValue = nominal,
            UpperDeviation = upper,
            LowerDeviation = lower,
            ToleranceValue = Math.Max(Math.Abs(upper), Math.Abs(lower))
        };
    }

    private static Primitive? CreatePrimitive(StructuredPrimitiveDto dto)
    {
        if (!Enum.TryParse<PrimitiveType>(dto.Type, ignoreCase: true, out var primitiveType))
            return null;

        var id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id;
        var sourceElementId = string.IsNullOrWhiteSpace(dto.SourceElementId) ? id : dto.SourceElementId;
        var name = dto.Name ?? id;

        Primitive? primitive = primitiveType switch
        {
            PrimitiveType.Point => CreatePoint(dto, id, sourceElementId, name),
            PrimitiveType.Line => CreateLine(dto, id, sourceElementId, name),
            PrimitiveType.Circle => CreateCircle(dto, id, sourceElementId, name),
            PrimitiveType.Arc => CreateArc(dto, id, sourceElementId, name),
            PrimitiveType.Plane => CreatePlane(dto, id, sourceElementId, name),
            PrimitiveType.Cylinder => CreateCylinder(dto, id, sourceElementId, name),
            PrimitiveType.Sphere => CreateSphere(dto, id, sourceElementId, name),
            PrimitiveType.Cone => CreateCone(dto, id, sourceElementId, name),
            PrimitiveType.Curve2D => CreateCurve2D(dto, id, sourceElementId, name),
            PrimitiveType.Surface3D => CreateSurface3D(dto, id, sourceElementId, name),
            _ => null
        };

        if (primitive is not null)
            primitive.SourceAreaMm2 = dto.Area;

        return primitive;
    }

    private static Tolerance? CreateTolerance(StructuredToleranceDto dto)
    {
        if (!Enum.TryParse<ToleranceType>(dto.Type, ignoreCase: true, out var toleranceType))
            return null;

        var id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id;
        var sourceElementId = string.IsNullOrWhiteSpace(dto.SourceElementId) ? id : dto.SourceElementId;
        var standard = ParseEnum(dto.ToleranceStandard, ToleranceStandard.ASME);

        return toleranceType switch
        {
            ToleranceType.Dimensional => new DimensionalTolerance
            {
                Id = id,
                Name = dto.Name ?? id,
                SourceElementId = sourceElementId,
                ToleranceStandard = standard,
                ToleranceValue = dto.ToleranceValue ?? 0,
                DimensionType = ParseEnum(dto.DimensionType, DimensionType.Linear),
                NominalValue = dto.NominalValue ?? 0,
                UpperDeviation = dto.UpperDeviation ?? 0,
                LowerDeviation = dto.LowerDeviation ?? 0
            },
            ToleranceType.Geometric => new GeometricTolerance
            {
                Id = id,
                Name = dto.Name ?? id,
                SourceElementId = sourceElementId,
                ToleranceStandard = standard,
                ToleranceValue = dto.ToleranceValue ?? 0,
                Characteristic = ParseEnum(dto.Characteristic, GdntCharacteristic.Position),
                ZoneShape = ParseEnum(dto.ZoneShape, ToleranceZoneShape.None),
                MaterialCondition = ParseEnum(dto.MaterialCondition, MaterialCondition.RFS),
                Datums = dto.Datums
                    .Where(datum => !string.IsNullOrWhiteSpace(datum.Label))
                    .Select(datum => new DatumReference
                    {
                        Label = datum.Label,
                        MaterialCondition = ParseEnum(datum.MaterialCondition, MaterialCondition.RFS)
                    })
                    .ToList()
            },
            _ => null
        };
    }

    private static PointPrimitive? CreatePoint(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        var point = dto.Point ?? dto.Center;
        return point is null ? null : new PointPrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            X = Get(point, 0),
            Y = Get(point, 1),
            Z = Get(point, 2)
        };
    }

    private static LinePrimitive? CreateLine(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        var start = dto.Start ?? dto.Point;
        var direction = dto.Direction;
        if (direction is null && dto.Start is not null && dto.End is not null)
        {
            direction =
            [
                Get(dto.End, 0) - Get(dto.Start, 0),
                Get(dto.End, 1) - Get(dto.Start, 1),
                Get(dto.End, 2) - Get(dto.Start, 2)
            ];
        }

        return start is null || direction is null ? null : new LinePrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            StartX = Get(start, 0),
            StartY = Get(start, 1),
            StartZ = Get(start, 2),
            DirX = Get(direction, 0),
            DirY = Get(direction, 1),
            DirZ = Get(direction, 2)
        };
    }

    private static CirclePrimitive? CreateCircle(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        if (dto.Center is null || dto.Radius is null)
            return null;

        var normal = dto.Normal ?? [0, 0, 1];
        return new CirclePrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            CenterX = Get(dto.Center, 0),
            CenterY = Get(dto.Center, 1),
            CenterZ = Get(dto.Center, 2),
            Radius = dto.Radius.Value,
            NormalX = Get(normal, 0),
            NormalY = Get(normal, 1),
            NormalZ = Get(normal, 2)
        };
    }

    private static ArcPrimitive? CreateArc(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        if (dto.Center is null || dto.Radius is null)
            return null;

        var normal = dto.Normal ?? [0, 0, 1];
        return new ArcPrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            CenterX = Get(dto.Center, 0),
            CenterY = Get(dto.Center, 1),
            CenterZ = Get(dto.Center, 2),
            Radius = dto.Radius.Value,
            StartAngleRad = dto.StartAngleRad ?? 0,
            EndAngleRad = dto.EndAngleRad ?? 0,
            NormalX = Get(normal, 0),
            NormalY = Get(normal, 1),
            NormalZ = Get(normal, 2)
        };
    }

    private static PlanePrimitive? CreatePlane(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        var point = dto.Point ?? dto.Center;
        var normal = dto.Normal ?? [0, 0, 1];
        return point is null ? null : new PlanePrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            PointX = Get(point, 0),
            PointY = Get(point, 1),
            PointZ = Get(point, 2),
            NormalX = Get(normal, 0),
            NormalY = Get(normal, 1),
            NormalZ = Get(normal, 2)
        };
    }

    private static CylinderPrimitive? CreateCylinder(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        if (dto.AxisPoint is null || dto.AxisDirection is null || dto.Radius is null)
            return null;

        return new CylinderPrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            AxisPointX = Get(dto.AxisCenter ?? dto.AxisPoint, 0),
            AxisPointY = Get(dto.AxisCenter ?? dto.AxisPoint, 1),
            AxisPointZ = Get(dto.AxisCenter ?? dto.AxisPoint, 2),
            AxisDirX = Get(dto.AxisDirection, 0),
            AxisDirY = Get(dto.AxisDirection, 1),
            AxisDirZ = Get(dto.AxisDirection, 2),
            Radius = dto.Radius.Value,
            AxisStartX = GetNullable(dto.AxisStart, 0),
            AxisStartY = GetNullable(dto.AxisStart, 1),
            AxisStartZ = GetNullable(dto.AxisStart, 2),
            AxisEndX = GetNullable(dto.AxisEnd, 0),
            AxisEndY = GetNullable(dto.AxisEnd, 1),
            AxisEndZ = GetNullable(dto.AxisEnd, 2),
            Length = dto.Length,
            StartAngleRad = dto.StartAngleRad,
            EndAngleRad = dto.EndAngleRad,
            AngularSpanRad = dto.AngularSpanRad,
            RadialReferenceX = GetNullable(dto.RadialReference, 0),
            RadialReferenceY = GetNullable(dto.RadialReference, 1),
            RadialReferenceZ = GetNullable(dto.RadialReference, 2),
            IsInnerSurface = dto.IsInnerSurface,
            SurfaceOrientation = dto.SurfaceOrientation,
            SourceElementIds = dto.SourceElementIds.Count > 0 ? dto.SourceElementIds : [sourceElementId]
        };
    }

    private static SpherePrimitive? CreateSphere(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        if (dto.Center is null || dto.Radius is null)
            return null;

        return new SpherePrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            CenterX = Get(dto.Center, 0),
            CenterY = Get(dto.Center, 1),
            CenterZ = Get(dto.Center, 2),
            Radius = dto.Radius.Value
        };
    }

    private static ConePrimitive? CreateCone(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        if (dto.Apex is null || dto.AxisDirection is null || dto.HalfAngleRad is null)
            return null;

        return new ConePrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            ApexX = Get(dto.Apex, 0),
            ApexY = Get(dto.Apex, 1),
            ApexZ = Get(dto.Apex, 2),
            AxisDirX = Get(dto.AxisDirection, 0),
            AxisDirY = Get(dto.AxisDirection, 1),
            AxisDirZ = Get(dto.AxisDirection, 2),
            HalfAngleRad = dto.HalfAngleRad.Value,
            AxisStartX = GetNullable(dto.AxisStart, 0),
            AxisStartY = GetNullable(dto.AxisStart, 1),
            AxisStartZ = GetNullable(dto.AxisStart, 2),
            AxisEndX = GetNullable(dto.AxisEnd, 0),
            AxisEndY = GetNullable(dto.AxisEnd, 1),
            AxisEndZ = GetNullable(dto.AxisEnd, 2),
            Length = dto.Length,
            StartAngleRad = dto.StartAngleRad,
            EndAngleRad = dto.EndAngleRad,
            AngularSpanRad = dto.AngularSpanRad,
            RefRadius = dto.RefRadius,
            RadiusStart = dto.RadiusStart,
            RadiusEnd = dto.RadiusEnd
        };
    }

    private static Curve2DPrimitive CreateCurve2D(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        return new Curve2DPrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            Points = dto.Points.Select(point => (Get(point, 0), Get(point, 1))).ToList(),
            IsClosed = dto.IsClosed ?? false,
            Degree = dto.Degree ?? 1
        };
    }

    private static Surface3DPrimitive CreateSurface3D(StructuredPrimitiveDto dto, string id, string sourceElementId, string name)
    {
        return new Surface3DPrimitive
        {
            Id = id,
            Name = name,
            SourceElementId = sourceElementId,
            Vertices = dto.Vertices.Select(point => (Get(point, 0), Get(point, 1), Get(point, 2))).ToList(),
            Triangles = dto.Triangles
                .Where(triangle => triangle.Length >= 3)
                .Select(triangle => (triangle[0], triangle[1], triangle[2]))
                .ToList(),
            SurfaceType = dto.SurfaceType
        };
    }

    private static bool PassesPrimitiveFilter(Primitive primitive, ImportOptions options)
    {
        return options.PrimitiveFilter is null
            || options.PrimitiveFilter.Length == 0
            || options.PrimitiveFilter.Contains(primitive.PrimitiveType);
    }

    private static JsonElement? ToJsonElement(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement element)
            return element;

        return JsonSerializer.SerializeToElement(value);
    }

    private static double[]? ReadPoint(JsonElement? element, string propertyName)
    {
        if (element is null
            || !element.Value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number)
            .Select(item => item.GetDouble())
            .ToArray();
    }

    private static double? ReadDouble(JsonElement? element, string propertyName)
    {
        return element is not null
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            ? property.GetDouble()
            : null;
    }

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        return element is not null
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        return element is not null
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string NormalizeType(string value)
    {
        return value.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
    }

    private static double Get(double[] values, int index)
    {
        return values.Length > index ? values[index] : 0;
    }

    private static double? GetNullable(double[]? values, int index)
    {
        return values is not null && values.Length > index ? values[index] : null;
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct
    {
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryReadFirstNumber(string text, out double value)
    {
        var number = new string(text
            .SkipWhile(ch => !char.IsDigit(ch) && ch != '-' && ch != '.')
            .TakeWhile(ch => char.IsDigit(ch) || ch == '-' || ch == '.')
            .ToArray());

        return double.TryParse(number, out value);
    }
}
