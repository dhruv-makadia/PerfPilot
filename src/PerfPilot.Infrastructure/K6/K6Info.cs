using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;

namespace PerfPilot.Infrastructure.K6;

/// <summary>Runs "k6 version" to verify the configured binary, caching the result briefly.</summary>
public sealed class K6Info(K6Paths paths, TimeProvider clock, ILogger<K6Info> logger) : IK6Info
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (K6Status Status, DateTimeOffset At)? _cached;

    public async Task<K6Status> GetStatusAsync(CancellationToken ct)
    {
        if (_cached is { } c && clock.GetUtcNow() - c.At < CacheFor)
        {
            return c.Status;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } c2 && clock.GetUtcNow() - c2.At < CacheFor)
            {
                return c2.Status;
            }
            var status = await ProbeAsync(ct);
            _cached = (status, clock.GetUtcNow());
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<K6Status> ProbeAsync(CancellationToken ct)
    {
        var binary = paths.BinaryPath;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(binary, "version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0
                ? new K6Status(true, binary, output.Trim(), null)
                : new K6Status(false, binary, null, $"'{binary} version' exited with code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning("k6 is not available at {Binary}: {Message}", binary, ex.Message);
            return new K6Status(false, binary, null, $"k6 was not found or could not run ('{binary}'): {ex.Message}");
        }
    }
}
