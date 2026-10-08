namespace PerfPilot.Core.Domain;

public enum RunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

public enum SlaMetric
{
    P95 = 0,
    P99 = 1,
    ErrorRate = 2,
    MinRps = 3,
}

/// <summary>One execution of a scenario. Holds a snapshot so later scenario edits don't change history.</summary>
public class TestRun
{
    public Guid Id { get; set; }

    public Guid? ScenarioId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public Guid? EnvironmentId { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string EnvironmentBaseUrl { get; set; } = string.Empty;
    public bool EnvironmentIsProduction { get; set; }

    public ScenarioDefinition ScenarioSnapshot { get; set; } = new();

    /// <summary>The generated k6 script that was (or will be) executed.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>The SLA thresholds compiled into the script, used to interpret the k6 summary.</summary>
    public List<ThresholdDefinition> ThresholdPlan { get; set; } = [];

    public RunStatus Status { get; set; }

    /// <summary>Overall SLA verdict; null until the run has results.</summary>
    public bool? ThresholdsPassed { get; set; }

    public string StartedBy { get; set; } = string.Empty;
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CancelledBy { get; set; }
    public string? WorkDirectory { get; set; }

    /// <summary>Raw k6 --summary-export JSON.</summary>
    public string? SummaryJson { get; set; }

    public RunMetrics Overall { get; set; } = new();
    public long ChecksPassed { get; set; }
    public long ChecksFailed { get; set; }
    public List<RunEndpointResult> EndpointResults { get; set; } = [];
    public List<RunThresholdResult> ThresholdResults { get; set; } = [];
    public List<TimeSeriesPoint> TimeSeries { get; set; } = [];

    public bool IsFinished => Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;
}

public class RunMetrics
{
    public long Count { get; set; }
    public long Failed { get; set; }
    public double AvgMs { get; set; }
    public double MinMs { get; set; }
    public double P50Ms { get; set; }
    public double P90Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double MaxMs { get; set; }

    /// <summary>Failed requests ratio, 0..1.</summary>
    public double ErrorRate { get; set; }

    public double Rps { get; set; }
}

public class RunEndpointResult
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public RunMetrics Metrics { get; set; } = new();
    public long ChecksPassed { get; set; }
    public long ChecksFailed { get; set; }
    public int SortOrder { get; set; }
}

public class RunThresholdResult
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }

    /// <summary>Null for global thresholds.</summary>
    public string? EndpointKey { get; set; }

    public string ScopeName { get; set; } = string.Empty;
    public SlaMetric Metric { get; set; }
    public double Limit { get; set; }
    public double? Actual { get; set; }
    public bool Passed { get; set; }
    public string K6Metric { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>A threshold compiled into a k6 script, e.g. http_req_duration{endpoint:ep1} p(95)&lt;=300.</summary>
public class ThresholdDefinition
{
    public string? EndpointKey { get; set; }
    public string ScopeName { get; set; } = string.Empty;
    public SlaMetric Metric { get; set; }
    public double Limit { get; set; }
    public string K6Metric { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
}

/// <summary>Aggregated metrics for one time bucket of a run.</summary>
public class TimeSeriesPoint
{
    /// <summary>Seconds since the first sample of the run (bucket start).</summary>
    public double T { get; set; }

    public int Vus { get; set; }
    public long Requests { get; set; }
    public double Rps { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }

    /// <summary>0..1</summary>
    public double ErrorRate { get; set; }
}
