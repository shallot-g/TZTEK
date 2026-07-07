using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IFileImportPipeline, FileImportPipeline>();
        services.AddSingleton<IProbeAssigner, ProbeAssigner>();
        services.AddSingleton<IMeasurementPlanner, MeasurementPlanner>();
        services.AddSingleton<IMeasurementTaskAssembler, MeasurementTaskAssembler>();
        services.AddSingleton<IPrimitiveToleranceService, PrimitiveToleranceService>();

        return services;
    }
}
