using System.Collections.Concurrent;
using System.Threading.Channels;
using PerfPilot.Core.Abstractions;

namespace PerfPilot.Infrastructure.K6;

/// <summary>In-process FIFO of run ids waiting for the k6 worker.</summary>
public sealed class RunQueue : IRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct) => _channel.Writer.WriteAsync(runId, ct);
}

/// <summary>Tracks executing runs so they can be cancelled from API requests.</summary>
public sealed class ActiveRunRegistry : IRunCanceller
{
    private readonly ConcurrentDictionary<Guid, RunCancellation> _active = new();

    public RunCancellation Register(Guid runId, CancellationToken hostStopping)
    {
        var cancellation = new RunCancellation(runId, CancellationTokenSource.CreateLinkedTokenSource(hostStopping), this);
        _active[runId] = cancellation;
        return cancellation;
    }

    public bool RequestCancel(Guid runId, string requestedBy)
    {
        if (!_active.TryGetValue(runId, out var cancellation))
        {
            return false;
        }
        cancellation.Cancel(requestedBy);
        return true;
    }

    internal void Remove(Guid runId) => _active.TryRemove(runId, out _);
}

public sealed class RunCancellation(Guid runId, CancellationTokenSource cts, ActiveRunRegistry registry) : IDisposable
{
    public CancellationToken Token => cts.Token;

    /// <summary>Who asked to cancel; null when cancellation came from server shutdown.</summary>
    public string? RequestedBy { get; private set; }

    public bool IsCancellationRequested => cts.IsCancellationRequested;

    internal void Cancel(string requestedBy)
    {
        RequestedBy ??= requestedBy;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished at the same moment.
        }
    }

    public void Dispose()
    {
        registry.Remove(runId);
        cts.Dispose();
    }
}
