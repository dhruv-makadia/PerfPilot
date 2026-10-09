using Microsoft.EntityFrameworkCore;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.Persistence.Repositories;

internal sealed class RunRepository(PerfPilotDbContext db) : IRunRepository
{
    public async Task<PagedResult<TestRun>> ListAsync(RunQuery query, CancellationToken ct)
    {
        var q = db.TestRuns.AsNoTracking();
        if (query.ScenarioId is { } scenarioId)
        {
            q = q.Where(r => r.ScenarioId == scenarioId);
        }
        if (query.EnvironmentId is { } environmentId)
        {
            q = q.Where(r => r.EnvironmentId == environmentId);
        }
        if (query.Status is { } status)
        {
            q = q.Where(r => r.Status == status);
        }

        var total = await q.CountAsync(ct);
        // Project away the large columns (script, summary JSON, series) for the list view.
        var items = await q.OrderByDescending(r => r.QueuedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new TestRun
            {
                Id = r.Id,
                ScenarioId = r.ScenarioId,
                ScenarioName = r.ScenarioName,
                EnvironmentId = r.EnvironmentId,
                EnvironmentName = r.EnvironmentName,
                EnvironmentBaseUrl = r.EnvironmentBaseUrl,
                EnvironmentIsProduction = r.EnvironmentIsProduction,
                ScenarioSnapshot = r.ScenarioSnapshot,
                Status = r.Status,
                ThresholdsPassed = r.ThresholdsPassed,
                StartedBy = r.StartedBy,
                QueuedAt = r.QueuedAt,
                StartedAt = r.StartedAt,
                EndedAt = r.EndedAt,
                Overall = r.Overall,
            })
            .ToListAsync(ct);
        return new PagedResult<TestRun>(items, total, query.Page, query.PageSize);
    }

    public Task<TestRun?> GetAsync(Guid id, CancellationToken ct) =>
        db.TestRuns.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<TestRun?> GetDetailAsync(Guid id, CancellationToken ct) =>
        db.TestRuns
            .AsNoTracking()
            .Include(r => r.EndpointResults)
            .Include(r => r.ThresholdResults)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<Guid>> ListIdsByStatusAsync(RunStatus status, CancellationToken ct) =>
        db.TestRuns.Where(r => r.Status == status).OrderBy(r => r.QueuedAt).Select(r => r.Id).ToListAsync(ct);

    public Task<List<Guid>> ListRunningOnInstanceAsync(string runnerInstance, CancellationToken ct) =>
        db.TestRuns
            .Where(r => r.Status == RunStatus.Running && (r.RunnerInstance == null || r.RunnerInstance == runnerInstance))
            .Select(r => r.Id)
            .ToListAsync(ct);

    // The conditional updates below run as single UPDATE ... WHERE statements, so they are safe when
    // several PerfPilot instances share the database.
    public async Task<bool> TryClaimAsync(Guid id, string runnerInstance, DateTimeOffset startedAt, string workDirectory, CancellationToken ct) =>
        await db.TestRuns
            .Where(r => r.Id == id && r.Status == RunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Running)
                .SetProperty(r => r.StartedAt, startedAt)
                .SetProperty(r => r.RunnerInstance, runnerInstance)
                .SetProperty(r => r.WorkDirectory, workDirectory), ct) == 1;

    public async Task<bool> TryCancelQueuedAsync(Guid id, string cancelledBy, DateTimeOffset endedAt, string message, CancellationToken ct) =>
        await db.TestRuns
            .Where(r => r.Id == id && r.Status == RunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Cancelled)
                .SetProperty(r => r.EndedAt, endedAt)
                .SetProperty(r => r.CancelledBy, cancelledBy)
                .SetProperty(r => r.ErrorMessage, message), ct) == 1;

    public async Task<bool> RequestCancelAsync(Guid id, string requestedBy, CancellationToken ct) =>
        await db.TestRuns
            .Where(r => r.Id == id && r.Status == RunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequestedBy, requestedBy), ct) == 1;

    public Task<string?> GetCancelRequestAsync(Guid id, CancellationToken ct) =>
        db.TestRuns.AsNoTracking().Where(r => r.Id == id).Select(r => r.CancelRequestedBy).FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, TestRun>> GetLatestRunsByScenarioAsync(IEnumerable<Guid> scenarioIds, CancellationToken ct)
    {
        var ids = scenarioIds.Cast<Guid?>().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var latest = await db.TestRuns.AsNoTracking()
            .Where(r => ids.Contains(r.ScenarioId))
            .GroupBy(r => r.ScenarioId)
            .Select(g => g.OrderByDescending(r => r.QueuedAt)
                .Select(r => new { r.Id, r.ScenarioId, r.Status, r.ThresholdsPassed, r.QueuedAt })
                .First())
            .ToListAsync(ct);
        return latest.ToDictionary(
            r => r.ScenarioId!.Value,
            r => new TestRun { Id = r.Id, ScenarioId = r.ScenarioId, Status = r.Status, ThresholdsPassed = r.ThresholdsPassed, QueuedAt = r.QueuedAt });
    }

    public async Task AddAsync(TestRun run, CancellationToken ct)
    {
        db.TestRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(TestRun run, CancellationToken ct) => db.SaveChangesAsync(ct);
}
