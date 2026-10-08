using PerfPilot.Core.Contracts;

namespace PerfPilot.Core.Abstractions;

/// <summary>Encrypts secrets at rest (ASP.NET Core Data Protection in production).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

/// <summary>The user performing the current request.</summary>
public interface ICurrentUser
{
    string Name { get; }
}

/// <summary>Performs server-side reachability checks against a target environment.</summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(ConnectionTestContext context, CancellationToken ct);
}

/// <summary>Downloads OpenAPI documents server-side (never from the browser).</summary>
public interface ISpecFetcher
{
    Task<FetchedSpec> FetchAsync(SpecFetchRequest request, CancellationToken ct);
}

public interface IOpenApiParser
{
    OpenApiParseResult Parse(string content);
}

/// <summary>Queues runs for the background k6 worker.</summary>
public interface IRunQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken ct);
}

/// <summary>Requests cancellation of a run that is currently executing.</summary>
public interface IRunCanceller
{
    bool RequestCancel(Guid runId, string requestedBy);
}

/// <summary>Pushes run status and live metrics to connected UIs.</summary>
public interface IRunNotifier
{
    Task RunStatusChangedAsync(RunStatusMessage message, CancellationToken ct);
    Task RunProgressAsync(RunProgressMessage message, CancellationToken ct);
}

/// <summary>Reports whether the configured k6 binary is usable.</summary>
public interface IK6Info
{
    Task<K6Status> GetStatusAsync(CancellationToken ct);
}

/// <summary>Executes an environment's login flow server-side and returns the extracted token.</summary>
public interface ILoginClient
{
    Task<string> GetTokenAsync(string baseUrl, ResolvedAuth auth, bool ignoreTlsErrors, TimeSpan timeout, CancellationToken ct);
}
