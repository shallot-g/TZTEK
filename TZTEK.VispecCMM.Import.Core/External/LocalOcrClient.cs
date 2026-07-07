namespace TZTEK.VispecCMM.Import.Core.External;

public sealed class LocalOcrClient : ILocalOcrClient
{
    private readonly ProcessRunner _processRunner;

    public LocalOcrClient()
        : this(new ProcessRunner())
    {
    }

    internal LocalOcrClient(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public Task<string> RecognizeAsync(string filePath, CancellationToken ct = default)
    {
        var script = Environment.GetEnvironmentVariable("TZTEK_DEEPSEEK_OCR_SCRIPT");
        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException("Set TZTEK_DEEPSEEK_OCR_SCRIPT to the local DeepSeek-OCR runner script.");

        var python = Environment.GetEnvironmentVariable("TZTEK_DEEPSEEK_OCR_PYTHON");
        if (string.IsNullOrWhiteSpace(python))
            python = Environment.GetEnvironmentVariable("TZTEK_PARSER_PYTHON");
        if (string.IsNullOrWhiteSpace(python))
            python = "python";

        var output = Environment.GetEnvironmentVariable("TZTEK_DEEPSEEK_OCR_OUTPUT");
        var arguments = new List<string> { script, "--input", filePath };
        if (!string.IsNullOrWhiteSpace(output))
            arguments.AddRange(["--output", output]);

        return _processRunner.RunAsync(python, arguments, Path.GetDirectoryName(script), ct);
    }
}
