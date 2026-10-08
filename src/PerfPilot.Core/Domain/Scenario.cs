using System.Text.Json.Serialization;

namespace PerfPilot.Core.Domain;

/// <summary>A saved load test: endpoints, load profile and SLA thresholds against one environment.</summary>
public class Scenario
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid EnvironmentId { get; set; }
    public TargetEnvironment? Environment { get; set; }

    /// <summary>The spec version the endpoints were picked from (informational).</summary>
    public Guid? ApiSpecId { get; set; }

    public ScenarioDefinition Definition { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>The full test definition. Persisted as JSON and snapshotted onto each run.</summary>
public class ScenarioDefinition
{
    public List<ScenarioEndpoint> Endpoints { get; set; } = [];
    public LoadProfile LoadProfile { get; set; } = new();
    public SlaThresholds GlobalThresholds { get; set; } = new();

    /// <summary>Pasted CSV (header row + data rows) used by csv parameters and {{csv.column}} placeholders.</summary>
    public string? CsvData { get; set; }
}

public class ScenarioEndpoint
{
    /// <summary>Optional display label; defaults to "METHOD /path".</summary>
    public string? Name { get; set; }

    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";

    /// <summary>Share of traffic in percent. All endpoint weights in a scenario sum to 100.</summary>
    public double Weight { get; set; }

    public List<ParameterValue> Parameters { get; set; } = [];
    public string? Body { get; set; }
    public string? ContentType { get; set; }
    public List<int> ExpectedStatusCodes { get; set; } = [200];
    public SlaThresholds Thresholds { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Method?.ToUpperInvariant()} {Path}" : Name.Trim();
}

public enum ParameterLocation
{
    Path = 0,
    Query = 1,
    Header = 2,
}

public enum ParameterValueMode
{
    /// <summary>Fixed value; may contain {{random.*}} / {{csv.*}} placeholders.</summary>
    Static = 0,

    /// <summary>Random row value from a column of the scenario CSV data.</summary>
    CsvColumn = 1,
}

public class ParameterValue
{
    public string Name { get; set; } = string.Empty;
    public ParameterLocation In { get; set; }
    public ParameterValueMode Mode { get; set; }
    public string? Value { get; set; }
    public string? CsvColumn { get; set; }
}

public enum LoadPreset
{
    Smoke = 0,
    Load = 1,
    Stress = 2,
    Spike = 3,
    Soak = 4,
    Custom = 5,
}

public class LoadProfile
{
    public LoadPreset Preset { get; set; } = LoadPreset.Smoke;
    public List<LoadStage> Stages { get; set; } = [];
    public double ThinkTimeMinSeconds { get; set; } = 1;
    public double ThinkTimeMaxSeconds { get; set; } = 2;
}

public class LoadStage
{
    /// <summary>k6 duration string, e.g. "30s", "5m", "1m30s".</summary>
    public string Duration { get; set; } = "1m";

    public int Target { get; set; }
}

public class SlaThresholds
{
    public double? P95Ms { get; set; }
    public double? P99Ms { get; set; }
    public double? MaxErrorRatePercent { get; set; }
    public double? MinRps { get; set; }

    [JsonIgnore]
    public bool IsEmpty => P95Ms is null && P99Ms is null && MaxErrorRatePercent is null && MinRps is null;
}
