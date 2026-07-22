using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TZTEK.VispecCMM.Demo.Api.Models;
using TZTEK.VispecCMM.Import.Core.DependencyInjection;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

public sealed class DemoSessionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".stp", ".step"
    };

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DemoSessionService> _logger;
    private readonly string _root;

    public DemoSessionService(IWebHostEnvironment environment, ILogger<DemoSessionService> logger)
    {
        _environment = environment;
        _logger = logger;
        _root = Path.Combine(Path.GetTempPath(), "TZTEK-VispecCMM-Demo");
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<DemoExampleDto> GetExamples()
    {
        var root = FindIntroductionDirectory();
        if (root is null)
            return [];

        return new[]
        {
            CreateExample(root, "cylinder", "圆柱.stp", "空心圆柱：98 个测点与自动安全 GOTO"),
            CreateExample(root, "complex", "B23CD83-05-02V1.0.stp", "复杂工件：多类型基元与大规模路径")
        }.Where(item => item is not null).Cast<DemoExampleDto>().ToList();
    }

    public async Task<DemoSessionDto> CreateFromUploadAsync(IFormFile file, CancellationToken ct)
    {
        ValidateFile(file.FileName);
        CleanupExpired();
        var entry = CreateEntry(file.FileName);
        await using (var output = File.Create(entry.InputPath))
            await file.CopyToAsync(output, ct);
        StartProcessing(entry);
        return entry.Dto;
    }

    public async Task<DemoSessionDto> CreateFromExampleAsync(string name, CancellationToken ct)
    {
        var example = ResolveExample(name) ?? throw new FileNotFoundException("找不到指定示例文件。");
        ValidateFile(example);
        CleanupExpired();
        var entry = CreateEntry(Path.GetFileName(example));
        await using (var source = File.OpenRead(example))
        await using (var output = File.Create(entry.InputPath))
            await source.CopyToAsync(output, ct);
        StartProcessing(entry);
        return entry.Dto;
    }

    public bool TryGet(string id, out DemoSessionDto session)
    {
        if (_sessions.TryGetValue(id, out var entry))
        {
            session = entry.Dto;
            return true;
        }
        session = null!;
        return false;
    }

    public bool TryGetModel(string id, out string path)
    {
        if (_sessions.TryGetValue(id, out var entry) && entry.ModelPath is not null)
        {
            path = entry.ModelPath;
            return true;
        }
        path = string.Empty;
        return false;
    }

    public bool Delete(string id)
    {
        if (!_sessions.TryRemove(id, out var entry))
            return false;
        TryDeleteDirectory(entry.Directory);
        return true;
    }

    private SessionEntry CreateEntry(string fileName)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(fileName);
        var entry = new SessionEntry
        {
            Directory = directory,
            InputPath = Path.Combine(directory, safeName),
            CreatedAt = DateTime.UtcNow,
            Dto = new DemoSessionDto { Id = id }
        };
        _sessions[id] = entry;
        return entry;
    }

    private void StartProcessing(SessionEntry entry)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Demo session {SessionId} failed", entry.Dto.Id);
                entry.Dto.Status = "Failed";
                entry.Dto.Error = ex.Message;
                entry.Dto.Stage = "处理失败";
            }
        });
    }

    private async Task ProcessAsync(SessionEntry entry)
    {
        entry.Dto.Status = "Processing";
        entry.Dto.Progress = 5;
        entry.Dto.Stage = "导入并解析文件";

        using var services = CreateAlgorithmServices();
        var service = services.GetRequiredService<IPrimitiveToleranceService>();
        var progress = new Progress<ImportProgress>(value =>
        {
            entry.Dto.Progress = Math.Clamp(5 + (int)Math.Round(value.PercentComplete / 2.0), 5, 55);
            entry.Dto.Stage = value.Stage;
        });
        var import = await service.ImportAsync(entry.InputPath, new ImportOptions(), progress);

        entry.Dto.Progress = 60;
        entry.Dto.Stage = "生成基础测量路径";
        var baseline = service.GenerateMeasurementTasks(CreateBaselineOptions()).FirstOrDefault()
            ?? throw new InvalidOperationException("没有生成基础测量任务。");

        entry.Dto.Progress = 72;
        entry.Dto.Stage = "生成优化测量路径";
        var optimized = service.GenerateMeasurementTasks(CreateOptimizedOptions()).FirstOrDefault()
            ?? throw new InvalidOperationException("没有生成优化测量任务。");

        entry.Dto.Progress = 82;
        entry.Dto.Stage = "生成可视化模型";
        entry.ModelPath = await StepMeshExporter.TryExportAsync(entry.InputPath, entry.Directory, _environment.ContentRootPath, _logger);

        entry.Dto.Progress = 94;
        entry.Dto.Stage = "整理可视化数据";
        entry.Dto.Result = VisualizationMapper.Map(
            entry.Dto.Id,
            import,
            baseline,
            optimized,
            entry.ModelPath is not null);
        entry.Dto.Progress = 100;
        entry.Dto.Stage = "已完成";
        entry.Dto.Status = "Completed";
    }

    private static ServiceProvider CreateAlgorithmServices()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddVispecCmmImportCore();
        return collection.BuildServiceProvider();
    }

    private static MeasurementPlanOptions CreateBaselineOptions() => new()
    {
        PathStrategy = PathOptimizationStrategy.ImportOrder,
        EnableCollisionAvoidance = true,
        EnableContinuousFeaturePath = false,
        EnableContinuousCylinderPath = false,
        EnableSinglePointSafetyPath = true,
        EnableCollisionCheck = false
    };

    private static MeasurementPlanOptions CreateOptimizedOptions() => new()
    {
        PathStrategy = PathOptimizationStrategy.TwoOpt,
        EnableCollisionAvoidance = true,
        EnableContinuousFeaturePath = true,
        EnableContinuousCylinderPath = true,
        EnableCollisionCheck = true,
        EnableGotoAvoidance = true,
        EnableAutoGlobalSafeGoto = true,
        EnableInterFeatureAutoGoto = true,
        EnablePrimitiveNarrowPhaseCollisionCheck = true
    };

    private static void ValidateFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
            throw new NotSupportedException($"当前演示平台只支持 STP 和 STEP，收到：{extension}");
    }

    private DemoExampleDto? CreateExample(string root, string id, string fileName, string description)
    {
        var path = Path.Combine(root, fileName);
        return File.Exists(path)
            ? new DemoExampleDto { Id = id, Name = fileName, Description = description }
            : null;
    }

    private string? ResolveExample(string id)
    {
        var root = FindIntroductionDirectory();
        if (root is null)
            return null;
        var fileName = id.ToLowerInvariant() switch
        {
            "cylinder" => "圆柱.stp",
            "complex" => "B23CD83-05-02V1.0.stp",
            _ => string.Empty
        };
        if (fileName.Length == 0)
            return null;
        var path = Path.Combine(root, fileName);
        return File.Exists(path) ? path : null;
    }

    private string? FindIntroductionDirectory()
    {
        var current = new DirectoryInfo(_environment.ContentRootPath);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "introduction_document");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        return null;
    }

    private void CleanupExpired()
    {
        var threshold = DateTime.UtcNow.AddHours(-2);
        foreach (var pair in _sessions.Where(pair => pair.Value.CreatedAt < threshold).ToList())
            Delete(pair.Key);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 临时文件会在后续清理周期继续处理。
        }
    }

    private sealed class SessionEntry
    {
        public required DemoSessionDto Dto { get; init; }
        public required string Directory { get; init; }
        public required string InputPath { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? ModelPath { get; set; }
    }
}
