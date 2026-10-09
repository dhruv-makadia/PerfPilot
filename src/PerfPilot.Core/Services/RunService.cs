using Microsoft.Extensions.Logging;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;
using PerfPilot.Core.Results;

namespace PerfPilot.Core.Services;

public sealed class RunService(
    IRunRepository runs,
    IScenarioRepository scenarios,
    IEnvironmentRepository environments,
    ScenarioValidator validator,
    IK6ScriptGenerator generator,
    IRunQueue queue,
    IRunCanceller canceller,
    IRunNotifier notifier,
    HtmlReportGenerator reportGenerator,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<RunService> logger)
{
    public async Task<RunDetailDto> StartAsync(StartRunRequest request, CancellationToken ct)
    {
        var scenario = await scenarios.GetAsync(request.ScenarioId, ct) ?? throw new NotFoundException("Scenario", request.ScenarioId);
        var env = scenario.Environment ?? await environments.GetAsync(scenario.EnvironmentId, ct)
            ?? throw new NotFoundException("Environment", scenario.EnvironmentId);

        // Re-validate at run time: safety caps may have been lowered since the scenario was saved.
        var errors = new ValidationErrors();
        validator.Validate(scenario.Name, scenario.Definition, errors);
        if (env.IsProduction && !string.Equals(request.ConfirmEnvironmentName?.Trim(), env.Name, StringComparison.Ordinal))
        {
            errors.Add("confirmEnvironmentName", $"'{env.Name}' is a production environment. Type the environment name exactly to confirm the run.");
        }
        errors.ThrowIfAny();

        var script = generator.Generate(new K6ScriptInput(scenario.Name, K6EnvironmentInfo.From(env), scenario.Definition));
        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            EnvironmentId = env.Id,
            EnvironmentName = env.Name,
            EnvironmentBaseUrl = env.BaseUrl,
            EnvironmentIsProduction = env.IsProduction,
            ScenarioSnapshot = scenario.Definition,
            Script = script.Content,
            ThresholdPlan = script.Thresholds.ToList(),
            Status = RunStatus.Queued,
            StartedBy = currentUser.Name,
            QueuedAt = clock.GetUtcNow(),
        };

        await runs.AddAsync(run, ct);
        logger.LogInformation("Run {RunId} queued for scenario {ScenarioName} on {EnvironmentName} (production: {IsProduction}) by {User}",
            run.Id, run.ScenarioName, run.EnvironmentName, run.EnvironmentIsProduction, run.StartedBy);

        await queue.EnqueueAsync(run.Id, ct);
        await notifier.RunStatusChangedAsync(new RunStatusMessage(run.Id, run.Status, null, null), ct);
        return ToDetail(run);
    }

    public async Task<PagedResult<RunListItemDto>> ListAsync(RunQuery query, CancellationToken ct)
    {
        var normalized = query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 200),
        };
        var page = await runs.ListAsync(normalized, ct);
        return new PagedResult<RunListItemDto>(page.Items.Select(ToListItem).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<RunDetailDto> GetAsync(Guid id, CancellationToken ct) => ToDetail(await GetDetailEntityAsync(id, ct));

    public async Task<RunDetailDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct) ?? throw new NotFoundException("Run", id);
        if (run.IsFinished)
        {
            throw new ConflictException($"The run has already finished with status {run.Status}.");
        }

        const string notStartedMessage = "Cancelled before it started.";
        if (run.Status == RunStatus.Queued &&
            await runs.TryCancelQueuedAsync(id, currentUser.Name, clock.GetUtcNow(), notStartedMessage, ct))
        {
            await notifier.RunStatusChangedAsync(new RunStatusMessage(id, RunStatus.Cancelled, null, notStartedMessage), ct);
            logger.LogInformation("Queued run {RunId} cancelled by {User}", id, currentUser.Name);
            return ToDetail(await GetDetailEntityAsync(id, ct));
        }

        // Running (or claimed by a runner a moment ago). The executing instance owns the run row and records the
        // cancellation when k6 stops: signal it directly if it's this instance, otherwise through the database.
        if (!canceller.RequestCancel(id, currentUser.Name) && !await runs.RequestCancelAsync(id, currentUser.Name, ct))
        {
            throw new ConflictException("The run has already finished. Refresh to see its status.");
        }
        logger.LogInformation("Cancellation of running run {RunId} requested by {User}", id, currentUser.Name);
        return ToDetail(await GetDetailEntityAsync(id, ct));
    }

    public async Task<(string Html, string FileName)> GetReportAsync(Guid id, CancellationToken ct)
    {
        var run = await GetDetailEntityAsync(id, ct);
        var html = reportGenerator.Generate(run);
        var stamp = (run.StartedAt ?? run.QueuedAt).ToString("yyyyMMdd-HHmm");
        var name = ScenarioService.ScriptFileName(run.ScenarioName).Replace(".k6.js", string.Empty);
        return (html, $"perfpilot-{name}-{stamp}.html");
    }

    public async Task<(string Script, string FileName)> GetScriptAsync(Guid id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct) ?? throw new NotFoundException("Run", id);
        return (run.Script, ScenarioService.ScriptFileName(run.ScenarioName));
    }

    public async Task<string> GetSummaryJsonAsync(Guid id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct) ?? throw new NotFoundException("Run", id);
        return run.SummaryJson ?? throw new NotFoundException("k6 summary for run", id);
    }

    private async Task<TestRun> GetDetailEntityAsync(Guid id, CancellationToken ct) =>
        await runs.GetDetailAsync(id, ct) ?? throw new NotFoundException("Run", id);

    private static double? Duration(TestRun r) =>
        r.StartedAt is { } s ? ((r.EndedAt ?? DateTimeOffset.UtcNow) - s).TotalSeconds : null;

    public static RunListItemDto ToListItem(TestRun r) => new(
        r.Id, r.ScenarioId, r.ScenarioName, r.EnvironmentId, r.EnvironmentName, r.EnvironmentIsProduction,
        r.Status, r.ThresholdsPassed, r.StartedBy, r.QueuedAt, r.StartedAt, r.EndedAt, Duration(r),
        r.ScenarioSnapshot.LoadProfile.Preset, r.Overall);

    public static RunDetailDto ToDetail(TestRun r) => new(
        r.Id, r.ScenarioId, r.ScenarioName, r.EnvironmentId, r.EnvironmentName, r.EnvironmentBaseUrl, r.EnvironmentIsProduction,
        r.Status, r.ThresholdsPassed, r.StartedBy, r.QueuedAt, r.StartedAt, r.EndedAt, Duration(r), r.ExitCode, r.ErrorMessage,
        r.CancelledBy, r.ScenarioSnapshot, r.Overall, r.ChecksPassed, r.ChecksFailed,
        r.EndpointResults.OrderBy(e => e.SortOrder).ToList(),
        r.ThresholdResults.OrderBy(t => t.SortOrder).ToList(),
        r.TimeSeries,
        r.SummaryJson is not null);
}
