namespace PerfPilot.Core.Options;

public sealed class K6Options
{
    public const string SectionName = "K6";

    /// <summary>Path to the k6 executable, or "k6" to resolve it from PATH.</summary>
    public string BinaryPath { get; set; } = "k6";

    /// <summary>Directory for generated scripts and k6 output. Relative paths resolve against the app content root.</summary>
    public string WorkDirectory { get; set; } = "work";

    /// <summary>How many k6 runs may execute at the same time. Additional runs wait in the queue.</summary>
    public int MaxConcurrentRuns { get; set; } = 1;

    /// <summary>Interval between live progress pushes to the UI.</summary>
    public double LiveUpdateIntervalSeconds { get; set; } = 2;

    /// <summary>How long to wait for k6 to stop after a graceful stop request before killing the process.</summary>
    public int GracefulStopTimeoutSeconds { get; set; } = 20;

    /// <summary>Delete the (potentially large) raw JSON metrics file after results are persisted.</summary>
    public bool DeleteRawOutputAfterRun { get; set; }
}

public sealed class SafetyOptions
{
    public const string SectionName = "Safety";

    public int MaxVus { get; set; } = 500;
    public int MaxDurationMinutes { get; set; } = 120;
}

public sealed class SpecImportOptions
{
    public const string SectionName = "SpecImport";

    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int MaxTimeoutSeconds { get; set; } = 300;
    public int MaxSpecSizeMb { get; set; } = 25;
}

public sealed class ConnectionTestOptions
{
    public const string SectionName = "ConnectionTest";

    public int TimeoutSeconds { get; set; } = 15;
}
