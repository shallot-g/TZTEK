using System.Text;

namespace TZTEK.VispecCMM.Import.Core.Prompts;

internal static class MeasurementDrawingPromptBuilder
{
    public static string BuildFromOcrText(string sourceFilePath, string ocrText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是三坐标测量图纸解析器。");
        builder.AppendLine("只输出 JSON，不要解释。");
        builder.AppendLine("不要编造坐标、尺寸、公差；无法确认的内容放入 uncertainItems。");
        builder.AppendLine("primitive.type 只能使用 Point, Line, Circle, Arc, Plane, Cylinder, Sphere, Cone, Curve2D, Surface3D。");
        builder.AppendLine("tolerance.type 只能使用 Dimensional 或 Geometric。");
        builder.AppendLine("dimensionType 只能使用 Linear, Diameter, Radius, Angle。");
        builder.AppendLine("GD&T characteristic 必须使用项目枚举英文名，例如 Flatness, Circularity, Position, ProfileOfSurface。");
        builder.AppendLine("输出字段固定为：");
        builder.AppendLine("""
{
  "primitives": [],
  "tolerances": [],
  "links": [],
  "datums": [],
  "coordinateSystems": [],
  "uncertainItems": []
}
""");
        builder.AppendLine("输入文件：");
        builder.AppendLine(sourceFilePath);
        builder.AppendLine("OCR 文本：");
        builder.AppendLine(ocrText);
        return builder.ToString();
    }
}
