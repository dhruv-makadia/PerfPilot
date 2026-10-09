namespace PerfPilot.Infrastructure.K6;

/// <summary>
/// Name of this PerfPilot instance (K6:InstanceName, or machine name + app folder). Stored on the runs it claims so that,
/// when several instances share a database, each one only recovers and executes its own runs.
/// </summary>
public sealed record RunnerIdentity(string Name);
