using System.Text.Json;
using TZTEK.VispecCMM.Import.Core.Serialization;

namespace TZTEK.VispecCMM.Import.Core.External;

public sealed class PythonParserClient : IPythonParserClient
{
    private readonly ProcessRunner _processRunner;

    public PythonParserClient()
        : this(new ProcessRunner())
    {
    }

    internal PythonParserClient(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<RawDocument> ParseAsync(
        string parserName,
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        var script = ResolveParserScriptPath();
        var python = ResolvePythonExecutable(script);
        var stdout = await _processRunner.RunAsync(
                python,
                [
                    script,
                    "--format", parserName,
                    "--input", filePath,
                    "--unit", options.Unit.ToString()
                ],
                Path.GetDirectoryName(script),
                ct)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(stdout);
        return RawDocumentJsonMapper.FromJson(document.RootElement, filePath);
    }

    private static string ResolveParserScriptPath()
    {
        var configured = Environment.GetEnvironmentVariable("TZTEK_IMPORT_PARSER_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var baseDirectory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "import_parser", "parse_file.py");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException(
            "Cannot find tools/import_parser/parse_file.py. Set TZTEK_IMPORT_PARSER_SCRIPT to the parser script path.");
    }

    private static string ResolvePythonExecutable(string script)
    {
        var configured = Environment.GetEnvironmentVariable("TZTEK_PARSER_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        // The repository ships a dedicated STEP environment. Using the system
        // Python here makes CadQuery appear missing even when the sidecar is installed.
        var parserDirectory = Path.GetDirectoryName(script);
        var repositoryRoot = parserDirectory is null
            ? null
            : Directory.GetParent(parserDirectory)?.Parent?.FullName;
        if (repositoryRoot is not null)
        {
            var bundledPython = Path.Combine(repositoryRoot, ".venv-step", "Scripts", "python.exe");
            if (File.Exists(bundledPython))
                return bundledPython;
        }

        return "python";
    }
}
