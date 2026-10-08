using Microsoft.AspNetCore.SignalR;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;

namespace PerfPilot.Api.Hubs;

/// <summary>Live run updates. Clients join "run:{id}" for one run's metrics, or "runs" for status changes of all runs.</summary>
public sealed class RunHub : Hub
{
    public const string AllRunsGroup = "runs";

    public static string RunGroup(Guid runId) => $"run:{runId:N}";

    public Task JoinRun(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, RunGroup(runId));

    public Task LeaveRun(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroup(runId));

    public Task JoinRunList() => Groups.AddToGroupAsync(Context.ConnectionId, AllRunsGroup);

    public Task LeaveRunList() => Groups.RemoveFromGroupAsync(Context.ConnectionId, AllRunsGroup);
}

public sealed class SignalRRunNotifier(IHubContext<RunHub> hub) : IRunNotifier
{
    public Task RunStatusChangedAsync(RunStatusMessage message, CancellationToken ct) =>
        hub.Clients.Groups(RunHub.RunGroup(message.RunId), RunHub.AllRunsGroup).SendAsync("runStatus", message, ct);

    public Task RunProgressAsync(RunProgressMessage message, CancellationToken ct) =>
        hub.Clients.Group(RunHub.RunGroup(message.RunId)).SendAsync("runProgress", message, ct);
}
