using Microsoft.EntityFrameworkCore;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.Persistence.Repositories;

internal sealed class SpecRepository(PerfPilotDbContext db) : ISpecRepository
{
    /// <summary>Spec rows without the (large) raw document.</summary>
    private IQueryable<ApiSpec> Summaries => db.ApiSpecs.AsNoTracking().Select(s => new ApiSpec
    {
        Id = s.Id,
        EnvironmentId = s.EnvironmentId,
        Version = s.Version,
        Title = s.Title,
        ApiVersion = s.ApiVersion,
        SpecFormat = s.SpecFormat,
        Source = s.Source,
        SourceLocation = s.SourceLocation,
        ImportedAt = s.ImportedAt,
        ImportedBy = s.ImportedBy,
        Warnings = s.Warnings,
        EndpointCount = s.EndpointCount,
    });

    public Task<List<ApiSpec>> ListVersionsAsync(Guid environmentId, CancellationToken ct) =>
        Summaries.Where(s => s.EnvironmentId == environmentId).OrderByDescending(s => s.Version).ToListAsync(ct);

    public Task<ApiSpec?> GetLatestSummaryAsync(Guid environmentId, CancellationToken ct) =>
        Summaries.Where(s => s.EnvironmentId == environmentId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, ApiSpec>> GetLatestSummariesAsync(IEnumerable<Guid> environmentIds, CancellationToken ct)
    {
        var ids = environmentIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var latestIds = await db.ApiSpecs.AsNoTracking()
            .Where(s => ids.Contains(s.EnvironmentId))
            .GroupBy(s => s.EnvironmentId)
            .Select(g => g.OrderByDescending(s => s.Version).Select(s => s.Id).First())
            .ToListAsync(ct);
        var specs = await Summaries.Where(s => latestIds.Contains(s.Id)).ToListAsync(ct);
        return specs.ToDictionary(s => s.EnvironmentId);
    }

    public Task<ApiSpec?> GetWithEndpointsAsync(Guid specId, CancellationToken ct) =>
        db.ApiSpecs.AsNoTracking()
            .Include(s => s.Endpoints.OrderBy(e => e.SortOrder))
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == specId, ct);

    public Task<string?> GetRawContentAsync(Guid specId, CancellationToken ct) =>
        db.ApiSpecs.AsNoTracking().Where(s => s.Id == specId).Select(s => s.RawContent).FirstOrDefaultAsync(ct);

    public async Task<int> GetNextVersionAsync(Guid environmentId, CancellationToken ct) =>
        (await db.ApiSpecs.Where(s => s.EnvironmentId == environmentId).MaxAsync(s => (int?)s.Version, ct) ?? 0) + 1;

    public async Task AddAsync(ApiSpec spec, CancellationToken ct)
    {
        db.ApiSpecs.Add(spec);
        await db.SaveChangesAsync(ct);
    }
}
