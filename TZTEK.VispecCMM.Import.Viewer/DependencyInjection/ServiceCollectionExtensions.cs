using Microsoft.Extensions.DependencyInjection;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;
using TZTEK.VispecCMM.Import.Viewer;

namespace TZTEK.VispecCMM.Import.Viewer.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVispecCmmViewer(this IServiceCollection services)
    {
        services.AddSingleton<IMeasurementSceneBuilder, MeasurementSceneBuilder>();
        return services;
    }
}
