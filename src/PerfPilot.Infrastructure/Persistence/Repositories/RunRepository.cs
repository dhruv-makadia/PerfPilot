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
            .Include(r => r.EndpointResults)
            .Include(r => r.ThresholdResults)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<Guid>> ListIdsByStatusAsync(RunStatus status, CancellationToken ct) =>
        db.TestRuns.Where(r => r.Status == status).OrderBy(r => r.QueuedAt).Select(r => r.Id).ToListAsync(ct);

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
