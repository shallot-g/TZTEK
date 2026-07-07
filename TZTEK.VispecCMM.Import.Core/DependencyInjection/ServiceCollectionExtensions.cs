using Microsoft.Extensions.DependencyInjection;
using TZTEK.VispecCMM.Import.Core.External;
using TZTEK.VispecCMM.Import.Core.Extraction;
using TZTEK.VispecCMM.Import.Core.Importers;
using TZTEK.VispecCMM.Import.Core.Pipeline;
using TZTEK.VispecCMM.Import.Core.Services;

namespace TZTEK.VispecCMM.Import.Core.DependencyInjection;

/// <summary>
/// Core 层依赖注入扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Vispec CMM 导入默认实现。
    /// </summary>
    public static IServiceCollection AddVispecCmmImportCore(this IServiceCollection services)
    {
        services.AddSingleton<IPythonParserClient, PythonParserClient>();
        services.AddSingleton<ILocalOcrClient, LocalOcrClient>();
        services.AddSingleton<ILocalLlmClient, LocalLlmClient>();

        services.AddSingleton<IFileFormatImporter, DxfImporter>();
        services.AddSingleton<IFileFormatImporter, PdfImporter>();
        services.AddSingleton<IFileFormatImporter, StepImporter>();
        services.AddSingleton<IPrimitiveToleranceExtractor, PrimitiveToleranceExtractor>();

        services.AddSingleton<IFileImportPipeline, FileImportPipeline>();
        services.AddSingleton<IProbeAssigner, ProbeAssigner>();
        services.AddSingleton<IMeasurementPlanner, MeasurementPlanner>();
        services.AddSingleton<IMeasurementTaskAssembler, MeasurementTaskAssembler>();
        services.AddSingleton<IPrimitiveToleranceService, PrimitiveToleranceService>();

        return services;
    }
}
