using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Abstractions;

public interface IEnvironmentRepository
{
    Task<List<TargetEnvironment>> ListAsync(CancellationToken ct);
    Task<TargetEnvironment?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct);
    Task<int> CountScenariosAsync(Guid environmentId, CancellationToken ct);
    Task AddAsync(TargetEnvironment environment, CancellationToken ct);
    Task UpdateAsync(TargetEnvironment environment, CancellationToken ct);
    Task DeleteAsync(TargetEnvironment environment, CancellationToken ct);
}

public interface ISpecRepository
{
    Task<List<ApiSpec>> ListVersionsAsync(Guid environmentId, CancellationToken ct);
    Task<ApiSpec?> GetLatestSummaryAsync(Guid environmentId, CancellationToken ct);
    Task<Dictionary<Guid, ApiSpec>> GetLatestSummariesAsync(IEnumerable<Guid> environmentIds, CancellationToken ct);
    Task<ApiSpec?> GetWithEndpointsAsync(Guid specId, CancellationToken ct);
    Task<string?> GetRawContentAsync(Guid specId, CancellationToken ct);
    Task<int> GetNextVersionAsync(Guid environmentId, CancellationToken ct);
    Task AddAsync(ApiSpec spec, CancellationToken ct);
}

public interface IScenarioRepository
{
    Task<List<Scenario>> ListAsync(Guid? environmentId, CancellationToken ct);
    Task<Scenario?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct);
    Task AddAsync(Scenario scenario, CancellationToken ct);
    Task UpdateAsync(Scenario scenario, CancellationToken ct);
    Task DeleteAsync(Scenario scenario, CancellationToken ct);
}

public interface IRunRepository
{
    Task<PagedResult<TestRun>> ListAsync(RunQuery query, CancellationToken ct);
    Task<TestRun?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Loads the run with endpoint results, threshold results and series.</summary>
    Task<TestRun?> GetDetailAsync(Guid id, CancellationToken ct);

    Task<List<Guid>> ListIdsByStatusAsync(RunStatus status, CancellationToken ct);
    Task<Dictionary<Guid, TestRun>> GetLatestRunsByScenarioAsync(IEnumerable<Guid> scenarioIds, CancellationToken ct);
    Task AddAsync(TestRun run, CancellationToken ct);
    Task UpdateAsync(TestRun run, CancellationToken ct);
}
