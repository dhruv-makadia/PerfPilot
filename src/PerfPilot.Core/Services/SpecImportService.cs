using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Options;

namespace PerfPilot.Core.Services;

/// <summary>Imports OpenAPI documents (by URL or upload) and stores them as versions per environment.</summary>
public sealed class SpecImportService(
    ISpecRepository specs,
    EnvironmentService environmentService,
    ISpecFetcher fetcher,
    ILoginClient loginClient,
    IOpenApiParser parser,
    IOptions<SpecImportOptions> options,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<SpecImportService> logger)
{
    private static readonly string[] AllowedExtensions = [".json", ".yaml", ".yml"];

    public async Task<SpecDetailDto> ImportFromUrlAsync(Guid environmentId, ImportSpecFromUrlRequest request, CancellationToken ct)
    {
        var env = await environmentService.GetEntityAsync(environmentId, ct);
        var url = string.IsNullOrWhiteSpace(request.Url) ? env.OpenApiUrl : request.Url.Trim();
        if (!UrlHelper.IsAbsoluteHttpUrl(url))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["url"] = ["Enter an absolute http(s) URL for the OpenAPI document, or set one on the environment."],
            });
        }

        var opts = options.Value;
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? opts.DefaultTimeoutSeconds, 1, opts.MaxTimeoutSeconds);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (request.UseEnvironmentAuth)
        {
            var auth = environmentService.ResolveAuth(env);
            switch (auth.Type)
            {
                case AuthType.BearerToken when auth.Secret is not null:
                    headers["Authorization"] = "Bearer " + auth.Secret;
                    break;
                case AuthType.ApiKey when auth.Secret is not null && auth.ApiKeyHeaderName is not null:
                    headers[auth.ApiKeyHeaderName] = auth.Secret;
                    break;
                case AuthType.LoginFlow:
                    var token = await loginClient.GetTokenAsync(env.BaseUrl, auth, request.IgnoreTlsErrors || env.IgnoreTlsErrors, timeout, ct);
                    headers[auth.LoginTokenHeaderName] = auth.LoginTokenPrefix + token;
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AuthHeaderName))
        {
            headers[request.AuthHeaderName.Trim()] = request.AuthHeaderValue ?? string.Empty;
        }

        logger.LogInformation("Fetching OpenAPI spec for environment {EnvironmentId} from {SpecUrl} (timeout {Timeout}s, ignoreTls {IgnoreTls})",
            env.Id, url, timeoutSeconds, request.IgnoreTlsErrors);
        var fetched = await fetcher.FetchAsync(
            new SpecFetchRequest(url!, headers, timeout, request.IgnoreTlsErrors, opts.MaxSpecSizeMb * 1024L * 1024L), ct);

        return await SaveAsync(env, fetched.Content, SpecSource.Url, url!, ct);
    }

    public async Task<SpecDetailDto> ImportFromFileAsync(Guid environmentId, string fileName, Stream content, long length, CancellationToken ct)
    {
        var env = await environmentService.GetEntityAsync(environmentId, ct);
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Upload a .json, .yaml or .yml OpenAPI 3.x / Swagger 2.0 document."],
            });
        }

        var maxBytes = options.Value.MaxSpecSizeMb * 1024L * 1024L;
        if (length > maxBytes)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [$"The file is larger than the {options.Value.MaxSpecSizeMb} MB limit."],
            });
        }

        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        return await SaveAsync(env, text, SpecSource.Upload, Path.GetFileName(fileName!), ct);
    }

    public async Task<IReadOnlyList<SpecSummaryDto>> ListVersionsAsync(Guid environmentId, CancellationToken ct)
    {
        await environmentService.GetEntityAsync(environmentId, ct);
        return (await specs.ListVersionsAsync(environmentId, ct)).Select(ToSummary).ToList();
    }

    public async Task<SpecDetailDto> GetDetailAsync(Guid specId, CancellationToken ct)
    {
        var spec = await specs.GetWithEndpointsAsync(specId, ct) ?? throw new NotFoundException("Spec", specId);
        return ToDetail(spec);
    }

    public async Task<SpecDetailDto?> GetLatestDetailAsync(Guid environmentId, CancellationToken ct)
    {
        await environmentService.GetEntityAsync(environmentId, ct);
        var latest = await specs.GetLatestSummaryAsync(environmentId, ct);
        return latest is null ? null : await GetDetailAsync(latest.Id, ct);
    }

    public async Task<string> GetRawAsync(Guid specId, CancellationToken ct) =>
        await specs.GetRawContentAsync(specId, ct) ?? throw new NotFoundException("Spec", specId);

    private async Task<SpecDetailDto> SaveAsync(TargetEnvironment env, string content, SpecSource source, string location, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ValidationException("The OpenAPI document is empty.");
        }

        var parsed = parser.Parse(content);
        if (parsed.Endpoints.Count == 0)
        {
            throw new ValidationException("The document was parsed but contains no operations under 'paths'.");
        }

        var spec = new ApiSpec
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Version = await specs.GetNextVersionAsync(env.Id, ct),
            Title = string.IsNullOrWhiteSpace(parsed.Title) ? env.Name : parsed.Title,
            ApiVersion = parsed.ApiVersion,
            SpecFormat = parsed.SpecFormat,
            Source = source,
            SourceLocation = location,
            ImportedAt = clock.GetUtcNow(),
            ImportedBy = currentUser.Name,
            RawContent = content,
            Warnings = parsed.Warnings.ToList(),
            EndpointCount = parsed.Endpoints.Count,
        };
        var order = 0;
        foreach (var ep in parsed.Endpoints)
        {
            ep.Id = Guid.NewGuid();
            ep.ApiSpecId = spec.Id;
            ep.SortOrder = order++;
            spec.Endpoints.Add(ep);
        }

        await specs.AddAsync(spec, ct);
        logger.LogInformation("Imported spec v{Version} for environment {EnvironmentName}: {Title} ({Format}), {Count} endpoints, {WarningCount} warnings",
            spec.Version, env.Name, spec.Title, spec.SpecFormat, spec.EndpointCount, spec.Warnings.Count);
        return ToDetail(spec);
    }

    public static SpecSummaryDto ToSummary(ApiSpec s) => new(
        s.Id, s.EnvironmentId, s.Version, s.Title, s.ApiVersion, s.SpecFormat, s.Source, s.SourceLocation,
        s.ImportedAt, s.ImportedBy, s.EndpointCount, s.Warnings);

    private static SpecDetailDto ToDetail(ApiSpec spec)
    {
        var groups = spec.Endpoints
            .OrderBy(e => e.SortOrder)
            .GroupBy(e => e.Tag)
            .Select(g => new EndpointGroupDto(g.Key, g.Select(e => new EndpointDto(
                e.Id, e.Method, e.Path, e.OperationId, e.Summary, e.Description, e.Tag, e.Deprecated, e.Parameters,
                e.RequestBodyContentType, e.RequestBodySchema, e.SampleRequestBody, e.ResponseCodes)).ToList()))
            .ToList();
        return new SpecDetailDto(ToSummary(spec), groups);
    }
}
