using Microsoft.Extensions.Logging;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;

namespace PerfPilot.Core.Services;

public sealed class ScenarioService(
    IScenarioRepository scenarios,
    IEnvironmentRepository environments,
    IRunRepository runs,
    ScenarioValidator validator,
    IK6ScriptGenerator generator,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<ScenarioService> logger)
{
    public async Task<IReadOnlyList<ScenarioListItemDto>> ListAsync(Guid? environmentId, CancellationToken ct)
    {
        var list = await scenarios.ListAsync(environmentId, ct);
        var lastRuns = await runs.GetLatestRunsByScenarioAsync(list.Select(s => s.Id), ct);
        return list.Select(s =>
        {
            var stages = s.Definition.LoadProfile.Stages;
            var last = lastRuns.GetValueOrDefault(s.Id);
            return new ScenarioListItemDto(
                s.Id,
                s.Name,
                s.Description,
                s.EnvironmentId,
                s.Environment?.Name ?? string.Empty,
                s.Environment?.IsProduction ?? false,
                s.Definition.Endpoints.Count,
                s.Definition.LoadProfile.Preset,
                LoadProfilePresets.MaxTarget(stages),
                (int)LoadProfilePresets.TotalDuration(stages).TotalSeconds,
                s.UpdatedAt,
                last is null ? null : new LastRunDto(last.Id, last.Status, last.ThresholdsPassed, last.QueuedAt));
        }).ToList();
    }

    public async Task<ScenarioDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await GetEntityAsync(id, ct));

    public async Task<ScenarioDto> CreateAsync(SaveScenarioRequest request, CancellationToken ct)
    {
        var env = await ValidateAsync(request, null, ct);
        var now = clock.GetUtcNow();
        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            EnvironmentId = env.Id,
            Environment = env,
            ApiSpecId = request.ApiSpecId,
            Definition = NormalizeDefinition(request.Definition),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = currentUser.Name,
        };
        await scenarios.AddAsync(scenario, ct);
        logger.LogInformation("Created scenario {ScenarioName} ({ScenarioId})", scenario.Name, scenario.Id);
        return ToDto(scenario);
    }

    public async Task<ScenarioDto> UpdateAsync(Guid id, SaveScenarioRequest request, CancellationToken ct)
    {
        var scenario = await GetEntityAsync(id, ct);
        var env = await ValidateAsync(request, id, ct);
        scenario.Name = request.Name.Trim();
        scenario.Description = Normalize(request.Description);
        scenario.EnvironmentId = env.Id;
        scenario.Environment = env;
        scenario.ApiSpecId = request.ApiSpecId;
        scenario.Definition = NormalizeDefinition(request.Definition);
        scenario.UpdatedAt = clock.GetUtcNow();
        await scenarios.UpdateAsync(scenario, ct);
        logger.LogInformation("Updated scenario {ScenarioName} ({ScenarioId})", scenario.Name, scenario.Id);
        return ToDto(scenario);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var scenario = await GetEntityAsync(id, ct);
        await scenarios.DeleteAsync(scenario, ct);
        logger.LogInformation("Deleted scenario {ScenarioName} ({ScenarioId})", scenario.Name, scenario.Id);
    }

    public async Task<ScenarioDto> CloneAsync(Guid id, CancellationToken ct)
    {
        var source = await GetEntityAsync(id, ct);
        var baseName = $"Copy of {source.Name}";
        var name = baseName;
        for (var i = 2; await scenarios.NameExistsAsync(name, null, ct); i++)
        {
            name = $"{baseName} ({i})";
        }

        var now = clock.GetUtcNow();
        var clone = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = name.Length > 150 ? name[..150] : name,
            Description = source.Description,
            EnvironmentId = source.EnvironmentId,
            Environment = source.Environment,
            ApiSpecId = source.ApiSpecId,
            Definition = DeepCopy(source.Definition),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = currentUser.Name,
        };
        await scenarios.AddAsync(clone, ct);
        logger.LogInformation("Cloned scenario {SourceId} to {ScenarioName} ({ScenarioId})", source.Id, clone.Name, clone.Id);
        return ToDto(clone);
    }

    public async Task<ScriptPreviewDto> PreviewScriptAsync(Guid id, CancellationToken ct)
    {
        var scenario = await GetEntityAsync(id, ct);
        var env = scenario.Environment ?? await environments.GetAsync(scenario.EnvironmentId, ct)
            ?? throw new NotFoundException("Environment", scenario.EnvironmentId);
        return BuildPreview(scenario.Name, env, scenario.Definition);
    }

    /// <summary>Generates a script preview for an unsaved scenario (used by the builder's preview tab).</summary>
    public async Task<ScriptPreviewDto> PreviewScriptAsync(SaveScenarioRequest request, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        validator.Validate(request.Name, request.Definition, errors);
        var env = await environments.GetAsync(request.EnvironmentId, ct);
        if (env is null)
        {
            errors.Add("environmentId", "Select an environment.");
        }
        errors.ThrowIfAny();
        return BuildPreview(request.Name.Trim(), env!, NormalizeDefinition(request.Definition));
    }

    private ScriptPreviewDto BuildPreview(string name, TargetEnvironment env, ScenarioDefinition definition)
    {
        var script = generator.Generate(new K6ScriptInput(name, K6EnvironmentInfo.From(env), definition));
        return new ScriptPreviewDto(script.Content, script.RequiredEnvironmentVariables, ScriptFileName(name));
    }

    public static string ScriptFileName(string scenarioName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var slug = new string(scenarioName.Trim().ToLowerInvariant()
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray()).Trim('-');
        return (string.IsNullOrEmpty(slug) ? "scenario" : slug) + ".k6.js";
    }

    private async Task<Scenario> GetEntityAsync(Guid id, CancellationToken ct) =>
        await scenarios.GetAsync(id, ct) ?? throw new NotFoundException("Scenario", id);

    private async Task<TargetEnvironment> ValidateAsync(SaveScenarioRequest request, Guid? excludeId, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        validator.Validate(request.Name, request.Definition, errors);
        if (!string.IsNullOrWhiteSpace(request.Name) && await scenarios.NameExistsAsync(request.Name.Trim(), excludeId, ct))
        {
            errors.Add("name", $"A scenario named '{request.Name.Trim()}' already exists.");
        }
        var env = await environments.GetAsync(request.EnvironmentId, ct);
        if (env is null)
        {
            errors.Add("environmentId", "Select an environment.");
        }
        errors.ThrowIfAny();
        return env!;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static ScenarioDefinition NormalizeDefinition(ScenarioDefinition definition)
    {
        var copy = DeepCopy(definition);
        copy.CsvData = string.IsNullOrWhiteSpace(copy.CsvData) ? null : copy.CsvData.Trim('\r', '\n');
        foreach (var ep in copy.Endpoints)
        {
            ep.Name = Normalize(ep.Name);
            ep.Method = ep.Method.Trim().ToUpperInvariant();
            ep.Path = ep.Path.Trim();
            ep.ExpectedStatusCodes = ep.ExpectedStatusCodes.Distinct().ToList();
            ep.ContentType = Normalize(ep.ContentType);
            ep.Body = string.IsNullOrWhiteSpace(ep.Body) ? null : ep.Body;
            foreach (var p in ep.Parameters)
            {
                p.Name = p.Name.Trim();
                p.CsvColumn = Normalize(p.CsvColumn);
            }
        }
        foreach (var stage in copy.LoadProfile.Stages)
        {
            stage.Duration = stage.Duration.Trim();
        }
        return copy;
    }

    private static ScenarioDefinition DeepCopy(ScenarioDefinition definition) =>
        System.Text.Json.JsonSerializer.Deserialize<ScenarioDefinition>(
            System.Text.Json.JsonSerializer.Serialize(definition, JsonDefaults.Options), JsonDefaults.Options)!;

    private static ScenarioDto ToDto(Scenario s) => new(
        s.Id,
        s.Name,
        s.Description,
        s.EnvironmentId,
        s.Environment?.Name ?? string.Empty,
        s.Environment?.IsProduction ?? false,
        s.ApiSpecId,
        s.Definition,
        s.CreatedAt,
        s.UpdatedAt,
        s.CreatedBy);
}
