using System.Diagnostics;

namespace TZTEK.VispecCMM.Demo.Api.Services;

internal static class StepMeshExporter
{
    public static async Task<string?> TryExportAsync(
        string inputPath,
        string outputDirectory,
        string contentRoot,
        ILogger logger)
    {
        var extension = Path.GetExtension(inputPath);
        if (!extension.Equals(".stp", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".step", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var script = FindScript(contentRoot);
        if (script is null)
        {
            logger.LogWarning("Cannot find export_visual_mesh.py");
            return null;
        }

        var outputPath = Path.Combine(outputDirectory, "model.stl");
        var python = Environment.GetEnvironmentVariable("TZTEK_PARSER_PYTHON");
        if (string.IsNullOrWhiteSpace(python))
            python = "python";

        var info = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = Path.GetDirectoryName(script),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("--input");
        info.ArgumentList.Add(inputPath);
        info.ArgumentList.Add("--output");
        info.ArgumentList.Add(outputPath);

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 STEP 网格导出器。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                logger.LogWarning("STEP mesh export failed: {Error}", await stderr);
                return null;
            }
            _ = await stdout;
            return File.Exists(outputPath) ? outputPath : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "STEP mesh export failed");
            return null;
        }
    }

    private static string? FindScript(string contentRoot)
    {
        var current = new DirectoryInfo(contentRoot);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "import_parser", "export_visual_mesh.py");
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        return null;
    }
}
