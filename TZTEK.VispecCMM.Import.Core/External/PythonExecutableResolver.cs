namespace TZTEK.VispecCMM.Import.Core.External;

public static class PythonExecutableResolver
{
    public static string Resolve(string? parserScriptPath = null)
    {
        var configured = Environment.GetEnvironmentVariable("TZTEK_PARSER_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var repositoryRoot = ResolveRepositoryRoot(parserScriptPath);
        if (repositoryRoot is not null)
        {
            var bundledPython = Path.Combine(repositoryRoot, ".venv-step", "Scripts", "python.exe");
            if (File.Exists(bundledPython))
                return bundledPython;
        }

        return "python";
    }

    private static string? ResolveRepositoryRoot(string? parserScriptPath)
    {
        if (!string.IsNullOrWhiteSpace(parserScriptPath))
        {
            var parserDirectory = Path.GetDirectoryName(parserScriptPath);
            return parserDirectory is null
                ? null
                : Directory.GetParent(parserDirectory)?.Parent?.FullName;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "import_parser")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }
}
