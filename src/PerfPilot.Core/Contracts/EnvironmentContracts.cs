using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Contracts;

public sealed record EnvironmentDto(
    Guid Id,
    string Name,
    string BaseUrl,
    string? OpenApiUrl,
    bool IsProduction,
    bool IgnoreTlsErrors,
    AuthConfigDto Auth,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    SpecSummaryDto? LatestSpec);

/// <summary>Auth settings as returned to the UI. Secret values are never included, only a mask.</summary>
public sealed record AuthConfigDto(
    AuthType Type,
    bool HasSecret,
    string? SecretMask,
    string? ApiKeyHeaderName,
    string? LoginUrl,
    bool HasLoginBody,
    string? LoginTokenJsonPath,
    string LoginTokenHeaderName,
    string LoginTokenPrefix,
    LoginScope LoginScope);

public sealed record SaveEnvironmentRequest(
    string Name,
    string BaseUrl,
    string? OpenApiUrl,
    bool IsProduction,
    bool IgnoreTlsErrors,
    SaveAuthConfig? Auth);

/// <summary>
/// Auth settings posted by the UI. <see cref="Secret"/> and <see cref="LoginBody"/> left null or empty
/// keep the stored value; set <see cref="ClearSecret"/> to remove it.
/// </summary>
public sealed record SaveAuthConfig(
    AuthType Type,
    string? Secret,
    bool ClearSecret,
    string? ApiKeyHeaderName,
    string? LoginUrl,
    string? LoginBody,
    string? LoginTokenJsonPath,
    string? LoginTokenHeaderName,
    string? LoginTokenPrefix,
    LoginScope LoginScope);

/// <summary>Test-connection request; tests the posted (possibly unsaved) settings, merging stored secrets when an id is given.</summary>
public sealed record TestConnectionRequest(Guid? EnvironmentId, SaveEnvironmentRequest Environment);

public enum CheckSeverity
{
    Ok = 0,
    Warning = 1,
    Error = 2,
}

public sealed record ConnectionTestResult(bool Success, IReadOnlyList<ConnectionCheck> Checks);

public sealed record ConnectionCheck(
    string Name,
    string Url,
    CheckSeverity Severity,
    int? StatusCode,
    long LatencyMs,
    string? ErrorKind,
    string Message);

/// <summary>Resolved (decrypted) settings used by the connection tester.</summary>
public sealed record ConnectionTestContext(
    string BaseUrl,
    string? OpenApiUrl,
    bool IgnoreTlsErrors,
    ResolvedAuth Auth);

/// <summary>Decrypted auth settings for server-side use only. Never serialized to the UI.</summary>
public sealed record ResolvedAuth(
    AuthType Type,
    string? Secret,
    string? ApiKeyHeaderName,
    string? LoginUrl,
    string? LoginBody,
    string? LoginTokenJsonPath,
    string LoginTokenHeaderName,
    string LoginTokenPrefix,
    LoginScope LoginScope)
{
    public static ResolvedAuth None { get; } = new(AuthType.None, null, null, null, null, null, "Authorization", "Bearer ", LoginScope.PerVu);
}

public sealed record AppConfigDto(
    int MaxVus,
    int MaxDurationMinutes,
    string IdentityMode,
    string CurrentUser,
    K6Status K6);

public sealed record K6Status(bool Available, string BinaryPath, string? Version, string? Error);
