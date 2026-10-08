using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;
using PerfPilot.Core.Options;
using PerfPilot.Core.Results;
using PerfPilot.Core.Services;

namespace PerfPilot.Infrastructure.K6;

/// <summary>Executes one queued run: writes the script, runs k6, streams live metrics and persists results.</summary>
public sealed class RunExecutor(
    IServiceScopeFactory scopeFactory,
    ActiveRunRegistry registry,
    IRunNotifier notifier,
    K6Paths paths,
    IHttpClientFactory httpClientFactory,
    IOptions<K6Options> options,
    TimeProvider clock,
    ILogger<RunExecutor> logger)
{
    public const string ScriptFile = "script.js";
    public const string MetricsFile = "metrics.json";
    public const string SummaryFile = "summary.json";
    public const string LogFile = "k6-output.log";
    public const string K6ApiClientName = "k6-rest-api";

    public async Task ExecuteAsync(Guid runId, CancellationToken hostStopping)
    {
        // Register first so a cancel that races with start-up is still observed.
        using var cancellation = registry.Register(runId, hostStopping);
        await using var scope = scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var environments = scope.ServiceProvider.GetRequiredService<IEnvironmentRepository>();
        var environmentService = scope.ServiceProvider.GetRequiredService<EnvironmentService>();

        var run = await runs.GetAsync(runId, CancellationToken.None);
        if (run is null || run.Status != RunStatus.Queued)
        {
            logger.LogInformation("Skipping run {RunId}: status is {Status}", runId, run?.Status.ToString() ?? "missing");
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId });
        var workDir = paths.RunDirectory(runId);
        run.Status = RunStatus.Running;
        run.StartedAt = clock.GetUtcNow();
        run.WorkDirectory = workDir;
        await runs.UpdateAsync(run, CancellationToken.None);
        await NotifyStatusAsync(run);
        logger.LogInformation("Run {RunId} started: scenario {ScenarioName} on {EnvironmentName}", runId, run.ScenarioName, run.EnvironmentName);

        try
        {
            var env = run.EnvironmentId is { } envId ? await environments.GetAsync(envId, CancellationToken.None) : null;
            if (env is null)
            {
                throw new InvalidOperationException("The environment for this run no longer exists.");
            }

            Directory.CreateDirectory(workDir);
            await File.WriteAllTextAsync(Path.Combine(workDir, ScriptFile), run.Script, new UTF8Encoding(false), CancellationToken.None);

            var envVars = BuildSecretEnvironment(environmentService.ResolveAuth(env));
            var endpoints = K6ScriptGenerator.DescribeEndpoints(run.ScenarioSnapshot);
            var plannedSeconds = LoadProfilePresets.TotalDuration(run.ScenarioSnapshot.LoadProfile.Stages).TotalSeconds;
            var aggregator = new MetricsAggregator(Math.Max(2, Math.Ceiling(plannedSeconds / 300)));

            await ExecuteK6Async(run, workDir, envVars, aggregator, cancellation, hostStopping);

            run.TimeSeries = aggregator.BuildSeries();
            var summaryPath = Path.Combine(workDir, SummaryFile);
            if (File.Exists(summaryPath))
            {
                run.SummaryJson = await File.ReadAllTextAsync(summaryPath, CancellationToken.None);
                var parsed = K6SummaryParser.Parse(run.SummaryJson, run.ThresholdPlan, endpoints);
                RunResultMapper.Apply(run, parsed, endpoints);
            }

            if (run.Status == RunStatus.Running)
            {
                run.Status = RunStatus.Completed;
            }
            if (run.Status == RunStatus.Completed && run.SummaryJson is null)
            {
                run.Status = RunStatus.Failed;
                run.ErrorMessage ??= "k6 exited without writing a summary.";
            }
        }
        catch (Win32Exception ex)
        {
            run.Status = RunStatus.Failed;
            run.ErrorMessage = $"Could not start k6 at '{paths.BinaryPath}': {ex.Message}. Install k6 or set K6:BinaryPath in appsettings.json.";
            logger.LogError(ex, "Failed to start k6 for run {RunId}", runId);
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;
            run.ErrorMessage = ex.Message;
            logger.LogError(ex, "Run {RunId} failed", runId);
        }
        finally
        {
            run.EndedAt = clock.GetUtcNow();
            await runs.UpdateAsync(run, CancellationToken.None);
            await NotifyStatusAsync(run);
            logger.LogInformation("Run {RunId} finished: {Status}, exit code {ExitCode}, SLA passed {Passed}, {Requests} requests",
                runId, run.Status, run.ExitCode, run.ThresholdsPassed, run.Overall.Count);

            if (options.Value.DeleteRawOutputAfterRun)
            {
                TryDelete(Path.Combine(workDir, MetricsFile));
            }
        }
    }

    private async Task ExecuteK6Async(TestRun run, string workDir, Dictionary<string, string> envVars, MetricsAggregator aggregator,
        RunCancellation cancellation, CancellationToken hostStopping)
    {
        var opts = options.Value;
        await using var k6 = K6Process.Start(new K6StartInfo(paths.BinaryPath, workDir, ScriptFile, MetricsFile, SummaryFile, LogFile, envVars), logger);

        var tailTask = K6OutputTailer.TailAsync(Path.Combine(workDir, MetricsFile), aggregator.IngestLine, () => k6.HasExited, CancellationToken.None);
        using var liveCts = new CancellationTokenSource();
        var liveTask = PushLiveAsync(run.Id, aggregator, TimeSpan.FromSeconds(Math.Max(0.5, opts.LiveUpdateIntervalSeconds)), liveCts.Token);

        var cancelled = await Task.WhenAny(k6.ExitTask, Task.Delay(Timeout.Infinite, cancellation.Token)) != k6.ExitTask;
        if (cancelled)
        {
            logger.LogInformation("Stopping k6 for run {RunId} (requested by {User})", run.Id, cancellation.RequestedBy ?? "server shutdown");
            await k6.StopAsync(httpClientFactory.CreateClient(K6ApiClientName), TimeSpan.FromSeconds(Math.Max(1, opts.GracefulStopTimeoutSeconds)));
            await k6.ExitTask;
        }

        await tailTask;
        await liveCts.CancelAsync();
        await liveTask;
        await PushSnapshotAsync(run.Id, aggregator);

        run.ExitCode = k6.ExitCode;
        if (cancelled || cancellation.IsCancellationRequested)
        {
            run.Status = RunStatus.Cancelled;
            run.CancelledBy = cancellation.RequestedBy ?? "system";
            run.ErrorMessage = cancellation.RequestedBy is null
                ? "Stopped because the PerfPilot server was shutting down."
                : $"Cancelled by {cancellation.RequestedBy}.";
        }
        else if (k6.ExitCode is not (0 or 99))
        {
            // 0 = success, 99 = thresholds breached (a completed run with a failed verdict).
            run.Status = RunStatus.Failed;
            run.ErrorMessage = DescribeFailure(k6.ExitCode, k6.RecentOutput());
        }
    }

    private async Task PushLiveAsync(Guid runId, MetricsAggregator aggregator, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await PushSnapshotAsync(runId, aggregator);
            }
        }
        catch (OperationCanceledException)
        {
            // Run finished.
        }
    }

    private async Task PushSnapshotAsync(Guid runId, MetricsAggregator aggregator)
    {
        if (aggregator.TakeLiveSnapshot() is not { } s)
        {
            return;
        }
        try
        {
            await notifier.RunProgressAsync(new RunProgressMessage(runId, s.ElapsedSeconds, s.Vus, s.Rps, s.P95Ms, s.ErrorRate, s.TotalRequests, s.TotalFailed, s.Point), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push live progress for run {RunId}", runId);
        }
    }

    private async Task NotifyStatusAsync(TestRun run)
    {
        try
        {
            await notifier.RunStatusChangedAsync(new RunStatusMessage(run.Id, run.Status, run.ThresholdsPassed, run.ErrorMessage), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push status for run {RunId}", run.Id);
        }
    }

    /// <summary>Secrets reach k6 only as process environment variables, never in the script file.</summary>
    private static Dictionary<string, string> BuildSecretEnvironment(ResolvedAuth auth)
    {
        var vars = new Dictionary<string, string>();
        switch (auth.Type)
        {
            case AuthType.BearerToken when auth.Secret is not null:
                vars[K6EnvVars.BearerToken] = auth.Secret;
                break;
            case AuthType.ApiKey when auth.Secret is not null:
                vars[K6EnvVars.ApiKey] = auth.Secret;
                break;
            case AuthType.LoginFlow when auth.LoginBody is not null:
                vars[K6EnvVars.LoginBody] = auth.LoginBody;
                break;
        }
        return vars;
    }

    public static string DescribeFailure(int exitCode, IReadOnlyList<string> output)
    {
        var reason = exitCode switch
        {
            104 => "k6 rejected the configuration",
            105 => "k6 was aborted externally",
            106 => "k6 could not start its REST API (port in use?)",
            107 => "the script threw an exception",
            108 => "the script aborted the test",
            100 or 101 => "setup()/teardown() timed out",
            _ => $"k6 exited with code {exitCode}",
        };
        var errors = output.Where(l => l.Contains("level=error", StringComparison.OrdinalIgnoreCase) || l.StartsWith("ERRO", StringComparison.Ordinal)).TakeLast(5).ToList();
        var details = errors.Count > 0 ? errors : output.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(8).ToList();
        var message = details.Count > 0 ? $"{reason}: {string.Join(" | ", details)}" : reason + ".";
        return message.Length > 4000 ? message[..4000] : message;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
