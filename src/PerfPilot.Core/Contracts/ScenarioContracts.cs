using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Contracts;

public sealed record ScenarioDto(
    Guid Id,
    string Name,
    string? Description,
    Guid EnvironmentId,
    string EnvironmentName,
    bool EnvironmentIsProduction,
    Guid? ApiSpecId,
    ScenarioDefinition Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CreatedBy);

public sealed record ScenarioListItemDto(
    Guid Id,
    string Name,
    string? Description,
    Guid EnvironmentId,
    string EnvironmentName,
    bool EnvironmentIsProduction,
    int EndpointCount,
    LoadPreset Preset,
    int MaxVus,
    int TotalDurationSeconds,
    DateTimeOffset UpdatedAt,
    LastRunDto? LastRun);

public sealed record LastRunDto(Guid Id, RunStatus Status, bool? ThresholdsPassed, DateTimeOffset QueuedAt);

public sealed record SaveScenarioRequest(
    string Name,
    string? Description,
    Guid EnvironmentId,
    Guid? ApiSpecId,
    ScenarioDefinition Definition);

public sealed record ScriptPreviewDto(string Script, IReadOnlyList<string> RequiredEnvironmentVariables, string FileName);

public sealed record LoadPresetDto(LoadPreset Preset, string Description, IReadOnlyList<LoadStage> Stages, double ThinkTimeMinSeconds, double ThinkTimeMaxSeconds);
