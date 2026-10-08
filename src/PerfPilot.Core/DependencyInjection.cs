using Microsoft.Extensions.DependencyInjection;
using PerfPilot.Core.K6;
using PerfPilot.Core.Results;
using PerfPilot.Core.Services;

namespace PerfPilot.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddPerfPilotCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IK6ScriptGenerator, K6ScriptGenerator>();
        services.AddSingleton<ScenarioValidator>();
        services.AddSingleton<HtmlReportGenerator>();
        services.AddScoped<EnvironmentService>();
        services.AddScoped<SpecImportService>();
        services.AddScoped<ScenarioService>();
        services.AddScoped<RunService>();
        return services;
    }
}
