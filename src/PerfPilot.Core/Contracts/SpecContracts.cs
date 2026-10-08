using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Contracts;

public sealed record ImportSpecFromUrlRequest(
    string? Url,
    string? AuthHeaderName,
    string? AuthHeaderValue,
    bool UseEnvironmentAuth,
    int? TimeoutSeconds,
    bool IgnoreTlsErrors);

public sealed record SpecFetchRequest(
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    TimeSpan Timeout,
    bool IgnoreTlsErrors,
    long MaxBytes);

public sealed record FetchedSpec(string Content, string? ContentType, long LatencyMs);

public sealed record OpenApiParseResult(
    string Title,
    string? ApiVersion,
    string SpecFormat,
    IReadOnlyList<ApiEndpoint> Endpoints,
    IReadOnlyList<string> Warnings);

public sealed record SpecSummaryDto(
    Guid Id,
    Guid EnvironmentId,
    int Version,
    string Title,
    string? ApiVersion,
    string SpecFormat,
    SpecSource Source,
    string SourceLocation,
    DateTimeOffset ImportedAt,
    string? ImportedBy,
    int EndpointCount,
    IReadOnlyList<string> Warnings);

public sealed record SpecDetailDto(SpecSummaryDto Spec, IReadOnlyList<EndpointGroupDto> Groups);

public sealed record EndpointGroupDto(string Tag, IReadOnlyList<EndpointDto> Endpoints);

public sealed record EndpointDto(
    Guid Id,
    string Method,
    string Path,
    string? OperationId,
    string? Summary,
    string? Description,
    string Tag,
    bool Deprecated,
    IReadOnlyList<EndpointParameter> Parameters,
    string? RequestBodyContentType,
    string? RequestBodySchema,
    string? SampleRequestBody,
    IReadOnlyList<string> ResponseCodes);
