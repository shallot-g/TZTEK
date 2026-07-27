using System.Text.Json;

namespace TZTEK.VispecCMM.Demo.Api.Services;

internal sealed class PdfPageRenderer
{
    private readonly ProcessRunner _runner = new();

    public async Task<IReadOnlyList<RenderedDrawingPage>> RenderPagesAsync(string pdfPath, string outputDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var python = Environment.GetEnvironmentVariable("TZTEK_PARSER_PYTHON") ?? "python";
        var script = ResolveScript();
        var manifest = Path.Combine(outputDirectory, "drawing-pages.json");
        var dpi = int.TryParse(Environment.GetEnvironmentVariable("ARK_RENDER_DPI"), out var configured)
            ? Math.Clamp(configured, 120, 320)
            : 220;
        var maxEdge = int.TryParse(Environment.GetEnvironmentVariable("ARK_RENDER_MAX_EDGE"), out var configuredEdge)
            ? Math.Clamp(configuredEdge, 1200, 6500)
            : 6500;
        var maxPixels = int.TryParse(Environment.GetEnvironmentVariable("ARK_RENDER_MAX_PIXELS"), out var configuredPixels)
            ? Math.Clamp(configuredPixels, 1_000_000, 32_000_000)
            : 32_000_000;
        await _runner.RunAsync(python,
            [script, "--input", pdfPath, "--output-dir", outputDirectory, "--dpi", dpi.ToString(), "--max-edge", maxEdge.ToString(), "--max-pixels", maxPixels.ToString(), "--manifest", manifest],
            Path.GetDirectoryName(script), ct);
        if (!File.Exists(manifest)) throw new InvalidOperationException("PDF 页面渲染未生成清单。");
        var pages = JsonSerializer.Deserialize<List<RenderedDrawingPage>>(await File.ReadAllTextAsync(manifest, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        if (pages.Count == 0) throw new InvalidOperationException("PDF 没有可识别页面。");
        var invalid = pages.FirstOrDefault(page => page.PixelCount > maxPixels || !File.Exists(page.Path));
        if (invalid is not null)
            throw new InvalidOperationException($"PDF 第 {invalid.PageNumber} 页渲染结果无效：{invalid.Width}x{invalid.Height}，限制 {maxPixels} 像素。");
        return pages;
    }

    private static string ResolveScript()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "tools", "drawing", "render_pdf.py");
            if (File.Exists(path)) return path;
            current = current.Parent;
        }
        throw new FileNotFoundException("找不到 PDF 渲染脚本 tools/drawing/render_pdf.py。");
    }
}

internal sealed class RenderedDrawingPage
{
    public int PageNumber { get; init; }
    public string Path { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long PixelCount { get; init; }
}

internal sealed class ProcessRunner
{
    public async Task<string> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory, CancellationToken ct)
    {
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException($"无法启动 Python：{fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        return output;
    }
}
