using PerfPilot.Core.Domain;

namespace PerfPilot.Core.K6;

/// <summary>Environment variables the generated script reads secrets and overrides from.</summary>
public static class K6EnvVars
{
    public const string BaseUrl = "PERFPILOT_BASE_URL";
    public const string BearerToken = "PERFPILOT_BEARER_TOKEN";
    public const string ApiKey = "PERFPILOT_API_KEY";
    public const string LoginBody = "PERFPILOT_LOGIN_BODY";

    /// <summary>JSON object of headers for the login request (optional; defaults to JSON content type).</summary>
    public const string LoginHeaders = "PERFPILOT_LOGIN_HEADERS";
}

/// <summary>Non-secret environment settings needed to generate a script.</summary>
public sealed record K6EnvironmentInfo(
    string Name,
    string BaseUrl,
    bool IgnoreTlsErrors,
    AuthType AuthType,
    string? ApiKeyHeaderName,
    string? LoginUrl,
    string? LoginTokenJsonPath,
    string LoginTokenHeaderName,
    string LoginTokenPrefix,
    LoginScope LoginScope)
{
    public static K6EnvironmentInfo From(TargetEnvironment e) => new(
        e.Name,
        e.BaseUrl,
        e.IgnoreTlsErrors,
        e.AuthType,
        e.ApiKeyHeaderName,
        e.LoginUrl,
        e.LoginTokenJsonPath,
        e.LoginTokenHeaderName,
        e.LoginTokenPrefix,
        e.LoginScope);
}

public sealed record K6ScriptInput(string ScenarioName, K6EnvironmentInfo Environment, ScenarioDefinition Definition);

public sealed record K6EndpointInfo(string Key, string Name, string Method, string Path, string CheckName);

public sealed record K6Script(
    string Content,
    IReadOnlyList<ThresholdDefinition> Thresholds,
    IReadOnlyList<K6EndpointInfo> Endpoints,
    IReadOnlyList<string> RequiredEnvironmentVariables);

public interface IK6ScriptGenerator
{
    K6Script Generate(K6ScriptInput input);
}
