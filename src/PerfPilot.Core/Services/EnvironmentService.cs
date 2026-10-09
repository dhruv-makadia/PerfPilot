using System.Text.Json;
using Microsoft.Extensions.Logging;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Services;

public sealed class EnvironmentService(
    IEnvironmentRepository environments,
    ISpecRepository specs,
    ISecretProtector protector,
    IConnectionTester connectionTester,
    TimeProvider clock,
    ILogger<EnvironmentService> logger)
{
    /// <summary>Fixed-length mask so the UI never learns the secret or its length.</summary>
    public const string SecretMask = "••••••••";

    public const int MaxLoginHeaders = 20;

    public async Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct)
    {
        var list = await environments.ListAsync(ct);
        var latest = await specs.GetLatestSummariesAsync(list.Select(e => e.Id), ct);
        return list.Select(e => ToDto(e, latest.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<EnvironmentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var env = await GetEntityAsync(id, ct);
        var latest = await specs.GetLatestSummaryAsync(id, ct);
        return ToDto(env, latest);
    }

    public async Task<TargetEnvironment> GetEntityAsync(Guid id, CancellationToken ct) =>
        await environments.GetAsync(id, ct) ?? throw new NotFoundException("Environment", id);

    public async Task<EnvironmentDto> CreateAsync(SaveEnvironmentRequest request, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        await ValidateNameAsync(request.Name, null, errors, ct);
        var env = new TargetEnvironment { Id = Guid.NewGuid(), CreatedAt = clock.GetUtcNow() };
        Apply(env, request, existing: null, errors);
        errors.ThrowIfAny();

        env.UpdatedAt = env.CreatedAt;
        await environments.AddAsync(env, ct);
        logger.LogInformation("Created environment {EnvironmentName} ({EnvironmentId})", env.Name, env.Id);
        return ToDto(env, null);
    }

    public async Task<EnvironmentDto> UpdateAsync(Guid id, SaveEnvironmentRequest request, CancellationToken ct)
    {
        var env = await GetEntityAsync(id, ct);
        var errors = new ValidationErrors();
        await ValidateNameAsync(request.Name, id, errors, ct);
        var snapshot = Clone(env);
        Apply(env, request, snapshot, errors);
        errors.ThrowIfAny();

        env.UpdatedAt = clock.GetUtcNow();
        await environments.UpdateAsync(env, ct);
        logger.LogInformation("Updated environment {EnvironmentName} ({EnvironmentId})", env.Name, env.Id);
        return ToDto(env, await specs.GetLatestSummaryAsync(id, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var env = await GetEntityAsync(id, ct);
        var scenarioCount = await environments.CountScenariosAsync(id, ct);
        if (scenarioCount > 0)
        {
            throw new ConflictException($"Environment '{env.Name}' is used by {scenarioCount} scenario(s). Delete or move those scenarios first.");
        }
        await environments.DeleteAsync(env, ct);
        logger.LogInformation("Deleted environment {EnvironmentName} ({EnvironmentId})", env.Name, env.Id);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(TestConnectionRequest request, CancellationToken ct)
    {
        TargetEnvironment? existing = null;
        if (request.EnvironmentId is { } id)
        {
            existing = await GetEntityAsync(id, ct);
        }

        var probe = existing is null ? new TargetEnvironment() : Clone(existing);
        var errors = new ValidationErrors();
        Apply(probe, request.Environment, existing, errors, requireName: false);
        errors.ThrowIfAny();

        var context = new ConnectionTestContext(probe.BaseUrl, probe.OpenApiUrl, probe.IgnoreTlsErrors, ResolveAuth(probe));
        return await connectionTester.TestAsync(context, ct);
    }

    /// <summary>Decrypts the environment's auth settings for server-side use (k6 env vars, spec fetch, connection test).</summary>
    public ResolvedAuth ResolveAuth(TargetEnvironment env)
    {
        string? Decrypt(string? value) => string.IsNullOrEmpty(value) ? null : protector.Unprotect(value);

        return new ResolvedAuth(
            env.AuthType,
            env.AuthType is AuthType.BearerToken or AuthType.ApiKey ? Decrypt(env.ProtectedSecret) : null,
            env.ApiKeyHeaderName,
            env.LoginUrl,
            env.AuthType == AuthType.LoginFlow ? Decrypt(env.ProtectedLoginBody) : null,
            env.LoginTokenJsonPath,
            env.LoginTokenHeaderName,
            env.LoginTokenPrefix,
            env.LoginScope,
            env.AuthType == AuthType.LoginFlow
                ? env.LoginHeaders.ToDictionary(h => h.Name, h => protector.Unprotect(h.ProtectedValue), StringComparer.OrdinalIgnoreCase)
                : null);
    }

    private async Task ValidateNameAsync(string? name, Guid? excludeId, ValidationErrors errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "Name is required.");
        }
        else if (name.Trim().Length > 100)
        {
            errors.Add("name", "Name must be 100 characters or fewer.");
        }
        else if (await environments.NameExistsAsync(name.Trim(), excludeId, ct))
        {
            errors.Add("name", $"An environment named '{name.Trim()}' already exists.");
        }
    }

    private void Apply(TargetEnvironment env, SaveEnvironmentRequest request, TargetEnvironment? existing, ValidationErrors errors, bool requireName = true)
    {
        if (requireName)
        {
            env.Name = (request.Name ?? string.Empty).Trim();
        }

        if (!UrlHelper.IsAbsoluteHttpUrl(request.BaseUrl))
        {
            errors.Add("baseUrl", "Base URL must be an absolute http(s) URL, e.g. https://api.staging.example.com.");
        }
        else
        {
            env.BaseUrl = UrlHelper.NormalizeBaseUrl(request.BaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(request.OpenApiUrl) && !UrlHelper.IsAbsoluteHttpUrl(request.OpenApiUrl))
        {
            errors.Add("openApiUrl", "OpenAPI URL must be an absolute http(s) URL.");
        }
        env.OpenApiUrl = string.IsNullOrWhiteSpace(request.OpenApiUrl) ? null : request.OpenApiUrl.Trim();
        env.IsProduction = request.IsProduction;
        env.IgnoreTlsErrors = request.IgnoreTlsErrors;

        var auth = request.Auth ?? new SaveAuthConfig(AuthType.None, null, false, null, null, null, null, null, null, LoginScope.PerVu);
        var typeUnchanged = existing is not null && existing.AuthType == auth.Type;
        env.AuthType = auth.Type;

        switch (auth.Type)
        {
            case AuthType.None:
                ClearAuth(env);
                break;

            case AuthType.BearerToken:
            case AuthType.ApiKey:
                env.ProtectedLoginBody = null;
                env.LoginHeaders = [];
                env.LoginUrl = null;
                env.LoginTokenJsonPath = null;
                if (!string.IsNullOrEmpty(auth.Secret) && auth.Secret != SecretMask)
                {
                    env.ProtectedSecret = protector.Protect(auth.Secret.Trim());
                }
                else if (auth.ClearSecret || !typeUnchanged)
                {
                    env.ProtectedSecret = null;
                }
                if (env.ProtectedSecret is null)
                {
                    errors.Add("auth.secret", auth.Type == AuthType.BearerToken ? "Bearer token is required." : "API key value is required.");
                }
                if (auth.Type == AuthType.ApiKey)
                {
                    if (string.IsNullOrWhiteSpace(auth.ApiKeyHeaderName))
                    {
                        errors.Add("auth.apiKeyHeaderName", "Header name is required for API key auth.");
                    }
                    env.ApiKeyHeaderName = auth.ApiKeyHeaderName?.Trim();
                }
                else
                {
                    env.ApiKeyHeaderName = null;
                }
                break;

            case AuthType.LoginFlow:
                env.ProtectedSecret = null;
                env.ApiKeyHeaderName = null;
                if (string.IsNullOrWhiteSpace(auth.LoginUrl) ||
                    (!UrlHelper.IsAbsoluteHttpUrl(auth.LoginUrl) && !auth.LoginUrl.Trim().StartsWith('/')))
                {
                    errors.Add("auth.loginUrl", "Login URL must be an absolute http(s) URL or a path starting with '/' relative to the base URL.");
                }
                env.LoginUrl = auth.LoginUrl?.Trim();

                var previousHeaders = typeUnchanged ? existing!.LoginHeaders : [];
                env.LoginHeaders = auth.LoginHeaders is null
                    ? previousHeaders.Select(h => new StoredHeader { Name = h.Name, ProtectedValue = h.ProtectedValue }).ToList()
                    : ApplyLoginHeaders(auth.LoginHeaders, previousHeaders, errors);

                // Only JSON bodies are validated; a configured Content-Type (e.g. form-urlencoded) allows other formats.
                var contentType = env.LoginHeaders.FirstOrDefault(h => h.Name.Equals(HeaderRules.ContentType, StringComparison.OrdinalIgnoreCase)) is { } ct
                    ? protector.Unprotect(ct.ProtectedValue)
                    : HeaderRules.DefaultLoginContentType;
                var bodyInvalid = false;
                if (!string.IsNullOrWhiteSpace(auth.LoginBody))
                {
                    if (HeaderRules.IsJson(contentType) && !IsValidJson(auth.LoginBody))
                    {
                        errors.Add("auth.loginBody", "Login body must be valid JSON. To send another format, add a Content-Type login header.");
                        bodyInvalid = true;
                    }
                    else
                    {
                        env.ProtectedLoginBody = protector.Protect(auth.LoginBody.Trim());
                    }
                }
                else if (!typeUnchanged)
                {
                    env.ProtectedLoginBody = null;
                }
                if (env.ProtectedLoginBody is null && !bodyInvalid)
                {
                    errors.Add("auth.loginBody", "Login request body is required.");
                }

                if (!JsonPathEvaluator.IsValid(auth.LoginTokenJsonPath))
                {
                    errors.Add("auth.loginTokenJsonPath", "Token JSONPath is required, e.g. $.access_token or $.data.token.");
                }
                env.LoginTokenJsonPath = auth.LoginTokenJsonPath?.Trim();
                env.LoginTokenHeaderName = string.IsNullOrWhiteSpace(auth.LoginTokenHeaderName) ? "Authorization" : auth.LoginTokenHeaderName.Trim();
                env.LoginTokenPrefix = auth.LoginTokenPrefix ?? "Bearer ";
                env.LoginScope = auth.LoginScope;
                break;

            default:
                errors.Add("auth.type", "Unknown auth type.");
                break;
        }
    }

    /// <summary>
    /// Builds the stored login headers from the posted list. A blank value keeps the stored value of the
    /// header with the same name (case-insensitive), so the UI never needs to know secret values.
    /// </summary>
    private List<StoredHeader> ApplyLoginHeaders(IReadOnlyList<HeaderInput> input, IReadOnlyList<StoredHeader> previous, ValidationErrors errors)
    {
        const string field = "auth.loginHeaders";
        var result = new List<StoredHeader>();
        if (input.Count > MaxLoginHeaders)
        {
            errors.Add(field, $"At most {MaxLoginHeaders} login headers are supported.");
            return result;
        }

        foreach (var header in input)
        {
            var name = header.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                if (!string.IsNullOrEmpty(header.Value))
                {
                    errors.Add(field, "Every login header needs a name.");
                }
                continue;
            }
            if (!HeaderRules.IsValidName(name))
            {
                errors.Add(field, $"'{name}' is not a valid header name.");
                continue;
            }
            if (HeaderRules.IsReserved(name))
            {
                errors.Add(field, $"The '{name}' header is set automatically and can't be configured.");
                continue;
            }
            if (result.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(field, $"Login header '{name}' is listed more than once.");
                continue;
            }

            string protectedValue;
            if (!string.IsNullOrEmpty(header.Value) && header.Value != SecretMask)
            {
                if (!HeaderRules.IsValidValue(header.Value))
                {
                    errors.Add(field, $"The value of login header '{name}' can't contain line breaks.");
                    continue;
                }
                protectedValue = protector.Protect(header.Value.Trim());
            }
            else if (previous.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } kept)
            {
                protectedValue = kept.ProtectedValue;
            }
            else
            {
                errors.Add(field, $"Enter a value for login header '{name}'.");
                continue;
            }
            result.Add(new StoredHeader { Name = name, ProtectedValue = protectedValue });
        }
        return result;
    }

    private static void ClearAuth(TargetEnvironment env)
    {
        env.ProtectedSecret = null;
        env.ProtectedLoginBody = null;
        env.LoginHeaders = [];
        env.ApiKeyHeaderName = null;
        env.LoginUrl = null;
        env.LoginTokenJsonPath = null;
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TargetEnvironment Clone(TargetEnvironment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        BaseUrl = e.BaseUrl,
        OpenApiUrl = e.OpenApiUrl,
        IsProduction = e.IsProduction,
        IgnoreTlsErrors = e.IgnoreTlsErrors,
        AuthType = e.AuthType,
        ProtectedSecret = e.ProtectedSecret,
        ApiKeyHeaderName = e.ApiKeyHeaderName,
        LoginUrl = e.LoginUrl,
        ProtectedLoginBody = e.ProtectedLoginBody,
        LoginHeaders = e.LoginHeaders.Select(h => new StoredHeader { Name = h.Name, ProtectedValue = h.ProtectedValue }).ToList(),
        LoginTokenJsonPath = e.LoginTokenJsonPath,
        LoginTokenHeaderName = e.LoginTokenHeaderName,
        LoginTokenPrefix = e.LoginTokenPrefix,
        LoginScope = e.LoginScope,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    public static EnvironmentDto ToDto(TargetEnvironment e, ApiSpec? latestSpec) => new(
        e.Id,
        e.Name,
        e.BaseUrl,
        e.OpenApiUrl,
        e.IsProduction,
        e.IgnoreTlsErrors,
        new AuthConfigDto(
            e.AuthType,
            e.ProtectedSecret is not null,
            e.ProtectedSecret is not null ? SecretMask : null,
            e.ApiKeyHeaderName,
            e.LoginUrl,
            e.ProtectedLoginBody is not null,
            e.LoginTokenJsonPath,
            e.LoginTokenHeaderName,
            e.LoginTokenPrefix,
            e.LoginScope,
            e.LoginHeaders.Select(h => h.Name).ToList()),
        e.CreatedAt,
        e.UpdatedAt,
        latestSpec is null ? null : SpecImportService.ToSummary(latestSpec));
}
