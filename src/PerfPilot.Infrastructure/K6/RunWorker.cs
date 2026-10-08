using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Options;

namespace PerfPilot.Infrastructure.K6;

/// <summary>Hosted service that pulls queued runs and executes them, up to K6:MaxConcurrentRuns at a time.</summary>
public sealed class RunWorker(
    RunQueue queue,
    RunExecutor executor,
    IServiceScopeFactory scopeFactory,
    IOptions<K6Options> options,
    TimeProvider clock,
    ILogger<RunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        var maxConcurrent = Math.Max(1, options.Value.MaxConcurrentRuns);
        using var slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        var active = new ConcurrentDictionary<Guid, Task>();
        logger.LogInformation("Run worker started (max concurrent runs: {Max})", maxConcurrent);

        try
        {
            await foreach (var runId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                active[runId] = Task.Run(async () =>
                {
                    try
                    {
                        await executor.ExecuteAsync(runId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Unhandled error executing run {RunId}", runId);
                    }
                    finally
                    {
                        active.TryRemove(runId, out _);
                        slots.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }

        // Executors observe the stopping token, stop k6 gracefully and save partial results.
        await Task.WhenAll(active.Values);
    }

    /// <summary>On start-up, fail runs interrupted by a restart and re-queue runs that never started.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();

            foreach (var id in await runs.ListIdsByStatusAsync(RunStatus.Running, ct))
            {
                var run = await runs.GetAsync(id, ct);
                if (run is null)
                {
                    continue;
                }
                run.Status = RunStatus.Failed;
                run.EndedAt = clock.GetUtcNow();
                run.ErrorMessage = "Interrupted: the PerfPilot server stopped while this run was in progress.";
                await runs.UpdateAsync(run, ct);
                logger.LogWarning("Marked interrupted run {RunId} as failed", id);
            }

            foreach (var id in await runs.ListIdsByStatusAsync(RunStatus.Queued, ct))
            {
                await queue.EnqueueAsync(id, ct);
                logger.LogInformation("Re-queued run {RunId}", id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to recover runs on start-up");
        }
    }
}
