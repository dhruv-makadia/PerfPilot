using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Options;
using PerfPilot.Infrastructure.Http;
using PerfPilot.Infrastructure.K6;
using PerfPilot.Infrastructure.OpenApi;
using PerfPilot.Infrastructure.Persistence;
using PerfPilot.Infrastructure.Persistence.Repositories;
using PerfPilot.Infrastructure.Security;

namespace PerfPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPerfPilotInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        services.Configure<K6Options>(configuration.GetSection(K6Options.SectionName));
        services.Configure<SafetyOptions>(configuration.GetSection(SafetyOptions.SectionName));
        services.Configure<SpecImportOptions>(configuration.GetSection(SpecImportOptions.SectionName));
        services.Configure<ConnectionTestOptions>(configuration.GetSection(ConnectionTestOptions.SectionName));

        var connectionString = configuration.GetConnectionString("PerfPilot")
            ?? throw new InvalidOperationException("Connection string 'PerfPilot' is not configured.");
        services.AddDbContext<PerfPilotDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

        var dataProtection = services.AddDataProtection().SetApplicationName("PerfPilot");
        if (string.Equals(configuration["DataProtection:Store"], "Database", StringComparison.OrdinalIgnoreCase))
        {
            // Shared key ring: every instance using this database (e.g. IIS and local dev) can decrypt the same secrets.
            // Keys are stored unencrypted in the DataProtectionKeys table, so restrict who can read the database.
            dataProtection.PersistKeysToDbContext<PerfPilotDbContext>();
        }
        else
        {
            var keysDirectory = configuration["DataProtection:KeysDirectory"] is { Length: > 0 } configured ? configured : "keys";
            if (!Path.IsPathRooted(keysDirectory))
            {
                keysDirectory = Path.Combine(contentRootPath, keysDirectory);
            }
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
            if (OperatingSystem.IsWindows())
            {
                dataProtection.ProtectKeysWithDpapi();
            }
        }

        // Identifies this instance on the runs it executes, so several instances can share one database safely.
        var instanceName = configuration["K6:InstanceName"] is { Length: > 0 } name
            ? name
            : $"{Environment.MachineName}:{Path.GetFullPath(contentRootPath).TrimEnd('\\', '/')}";
        services.AddSingleton(new RunnerIdentity(instanceName.Length > 300 ? instanceName[..300] : instanceName));

        services.AddHttpClient(HttpClientNames.Default)
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(ignoreTls: false));
        services.AddHttpClient(HttpClientNames.IgnoreTls)
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(ignoreTls: true));
        services.AddHttpClient(RunExecutor.K6ApiClientName);
        // Per-request timeouts are applied with cancellation tokens so they can be configured per call.
        services.ConfigureHttpClientDefaults(b => b.ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan));

        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<ISpecRepository, SpecRepository>();
        services.AddScoped<IScenarioRepository, ScenarioRepository>();
        services.AddScoped<IRunRepository, RunRepository>();

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<IOpenApiParser, OpenApiSpecParser>();
        services.AddSingleton<LoginClient>();
        services.AddSingleton<ILoginClient>(sp => sp.GetRequiredService<LoginClient>());
        services.AddSingleton<IConnectionTester, ConnectionTester>();
        services.AddSingleton<ISpecFetcher, SpecFetcher>();

        services.AddSingleton<K6Paths>();
        services.AddSingleton<IK6Info, K6Info>();
        services.AddSingleton<RunQueue>();
        services.AddSingleton<IRunQueue>(sp => sp.GetRequiredService<RunQueue>());
        services.AddSingleton<ActiveRunRegistry>();
        services.AddSingleton<IRunCanceller>(sp => sp.GetRequiredService<ActiveRunRegistry>());
        services.AddSingleton<RunExecutor>();
        services.AddHostedService<RunWorker>();
        return services;
    }

    private static SocketsHttpHandler CreateHandler(bool ignoreTls)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        if (ignoreTls)
        {
            // Opt-in per environment/import for internal staging certificates only.
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    }
}
