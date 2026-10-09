using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using PerfPilot.Api.ErrorHandling;
using PerfPilot.Api.Hubs;
using PerfPilot.Api.Identity;
using PerfPilot.Core;
using PerfPilot.Core.Abstractions;
using PerfPilot.Infrastructure;
using PerfPilot.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // As a Windows service the working directory is System32, so use the app folder as the content root.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
    });
    // No-op unless started by the Service Control Manager. Running as a service on Kestrel avoids IIS's
    // 3-concurrent-request limit on Windows client editions, which live-update connections would exhaust.
    builder.Host.UseWindowsService(o => o.ServiceName = "PerfPilot");

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Leave time for running k6 processes to stop gracefully and save partial results on shutdown.
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45));

    builder.Services.AddPerfPilotCore();
    builder.Services.AddPerfPilotInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

    builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection(IdentityOptions.SectionName));
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();

    var useWindowsAuth = builder.Configuration.GetSection(IdentityOptions.SectionName).Get<IdentityOptions>()?.IsWindows ?? false;
    if (useWindowsAuth)
    {
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        builder.Services.AddAuthorization(o => o.FallbackPolicy = o.DefaultPolicy);
    }
    else
    {
        builder.Services.AddAuthorization();
    }

    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 100L * 1024 * 1024);

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PerfPilotDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging(o =>
        o.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/api") ? Serilog.Events.LogEventLevel.Information : Serilog.Events.LogEventLevel.Debug);

    // The Angular UI is built into wwwroot and served from the same origin, so no CORS is configured or needed.
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (useWindowsAuth)
    {
        app.UseAuthentication();
    }
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<RunHub>("/hubs/runs");
    app.MapFallback("/api/{**path}", (HttpContext ctx) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"No API endpoint matches {ctx.Request.Path}."));
    app.MapFallbackToFile("index.html");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PerfPilot terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
