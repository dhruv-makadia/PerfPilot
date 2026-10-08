using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record RunQuery(Guid? ScenarioId, Guid? EnvironmentId, RunStatus? Status, int Page = 1, int PageSize = 25);

public sealed record StartRunRequest(Guid ScenarioId, string? ConfirmEnvironmentName);

public sealed record RunListItemDto(
    Guid Id,
    Guid? ScenarioId,
    string ScenarioName,
    Guid? EnvironmentId,
    string EnvironmentName,
    bool EnvironmentIsProduction,
    RunStatus Status,
    bool? ThresholdsPassed,
    string StartedBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds,
    LoadPreset Preset,
    RunMetrics Overall);

public sealed record RunDetailDto(
    Guid Id,
    Guid? ScenarioId,
    string ScenarioName,
    Guid? EnvironmentId,
    string EnvironmentName,
    string EnvironmentBaseUrl,
    bool EnvironmentIsProduction,
    RunStatus Status,
    bool? ThresholdsPassed,
    string StartedBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    double? DurationSeconds,
    int? ExitCode,
    string? ErrorMessage,
    string? CancelledBy,
    ScenarioDefinition ScenarioSnapshot,
    RunMetrics Overall,
    long ChecksPassed,
    long ChecksFailed,
    IReadOnlyList<RunEndpointResult> EndpointResults,
    IReadOnlyList<RunThresholdResult> ThresholdResults,
    IReadOnlyList<TimeSeriesPoint> TimeSeries,
    bool HasSummary);

public sealed record RunStatusMessage(Guid RunId, RunStatus Status, bool? ThresholdsPassed, string? ErrorMessage);

public sealed record RunProgressMessage(
    Guid RunId,
    double ElapsedSeconds,
    int Vus,
    double Rps,
    double P95Ms,
    double ErrorRate,
    long TotalRequests,
    long TotalFailed,
    TimeSeriesPoint Point);
