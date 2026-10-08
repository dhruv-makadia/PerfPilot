using Microsoft.EntityFrameworkCore;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.Persistence.Repositories;

internal sealed class EnvironmentRepository(PerfPilotDbContext db) : IEnvironmentRepository
{
    public Task<List<TargetEnvironment>> ListAsync(CancellationToken ct) =>
        db.Environments.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct);

    public Task<TargetEnvironment?> GetAsync(Guid id, CancellationToken ct) =>
        db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct) =>
        db.Environments.AnyAsync(e => e.Name == name && (excludeId == null || e.Id != excludeId), ct);

    public Task<int> CountScenariosAsync(Guid environmentId, CancellationToken ct) =>
        db.Scenarios.CountAsync(s => s.EnvironmentId == environmentId, ct);

    public async Task AddAsync(TargetEnvironment environment, CancellationToken ct)
    {
        db.Environments.Add(environment);
        await db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(TargetEnvironment environment, CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task DeleteAsync(TargetEnvironment environment, CancellationToken ct)
    {
        db.Environments.Remove(environment);
        await db.SaveChangesAsync(ct);
    }
}
