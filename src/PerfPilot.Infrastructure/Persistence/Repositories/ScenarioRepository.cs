using Microsoft.EntityFrameworkCore;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.Persistence.Repositories;

internal sealed class ScenarioRepository(PerfPilotDbContext db) : IScenarioRepository
{
    public Task<List<Scenario>> ListAsync(Guid? environmentId, CancellationToken ct) =>
        db.Scenarios.AsNoTracking()
            .Include(s => s.Environment)
            .Where(s => environmentId == null || s.EnvironmentId == environmentId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public Task<Scenario?> GetAsync(Guid id, CancellationToken ct) =>
        db.Scenarios.Include(s => s.Environment).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct) =>
        db.Scenarios.AnyAsync(s => s.Name == name && (excludeId == null || s.Id != excludeId), ct);

    public async Task AddAsync(Scenario scenario, CancellationToken ct)
    {
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(Scenario scenario, CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task DeleteAsync(Scenario scenario, CancellationToken ct)
    {
        db.Scenarios.Remove(scenario);
        await db.SaveChangesAsync(ct);
    }
}
