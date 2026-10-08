using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Exceptions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using ParameterLocation = Microsoft.OpenApi.Models.ParameterLocation;

namespace PerfPilot.Infrastructure.OpenApi;

/// <summary>Maps OpenAPI 3.x / Swagger 2.0 documents (JSON or YAML) to PerfPilot endpoints using Microsoft.OpenApi.Readers.</summary>
public sealed partial class OpenApiSpecParser : IOpenApiParser
{
    private const int MaxWarnings = 50;

    private static readonly JsonSerializerOptions DisplayJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"[""']?openapi[""']?\s*:\s*[""']?(?<v>\d+\.\d+(\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex OpenApiVersionRegex();

    [GeneratedRegex(@"[""']?swagger[""']?\s*:\s*[""']?(?<v>\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SwaggerVersionRegex();

    public OpenApiParseResult Parse(string content)
    {
        var head = content.Length > 8192 ? content[..8192] : content;
        var openApiMatch = OpenApiVersionRegex().Match(head);
        var swaggerMatch = SwaggerVersionRegex().Match(head);
        if (!openApiMatch.Success && !swaggerMatch.Success)
        {
            throw new ValidationException("This is not an OpenAPI 3.x or Swagger 2.0 document (no 'openapi' or 'swagger' version field found).");
        }

        var declaredVersion = openApiMatch.Success ? openApiMatch.Groups["v"].Value : swaggerMatch.Groups["v"].Value;
        var warnings = new List<string>();
        var toParse = content;

        // Microsoft.OpenApi.Readers 1.x reads 3.0; most 3.1 documents are structurally compatible, so read them as 3.0.
        if (openApiMatch.Success && declaredVersion.StartsWith("3.1", StringComparison.Ordinal))
        {
            var v = openApiMatch.Groups["v"];
            toParse = content[..v.Index] + "3.0.3" + content[(v.Index + v.Length)..];
            warnings.Add($"OpenAPI {declaredVersion} was read using OpenAPI 3.0 rules; 3.1-only features (e.g. type arrays, webhooks) may be ignored.");
        }

        OpenApiDocument document;
        OpenApiDiagnostic diagnostic;
        try
        {
            var reader = new OpenApiStringReader(new OpenApiReaderSettings
            {
                ReferenceResolution = ReferenceResolutionSetting.ResolveLocalReferences,
            });
            document = reader.Read(toParse, out diagnostic);
        }
        catch (OpenApiException ex)
        {
            throw new ValidationException($"The OpenAPI document could not be read: {ex.Message}");
        }

        if (document?.Paths is null || document.Paths.Count == 0)
        {
            var reason = diagnostic.Errors.Count > 0
                ? string.Join("; ", diagnostic.Errors.Take(5).Select(e => FormatError(e.Pointer, e.Message)))
                : "it has no paths.";
            throw new ValidationException($"The OpenAPI document could not be used: {reason}");
        }

        warnings.AddRange(diagnostic.Errors.Select(e => FormatError(e.Pointer, e.Message)));
        warnings.AddRange(diagnostic.Warnings.Select(e => FormatError(e.Pointer, e.Message)));
        if (warnings.Count > MaxWarnings)
        {
            var extra = warnings.Count - MaxWarnings;
            warnings = warnings.Take(MaxWarnings).Append($"…and {extra} more warning(s).").ToList();
        }

        var format = diagnostic.SpecificationVersion == OpenApiSpecVersion.OpenApi2_0
            ? $"Swagger {declaredVersion}"
            : $"OpenAPI {declaredVersion}";

        return new OpenApiParseResult(
            document.Info?.Title ?? string.Empty,
            document.Info?.Version,
            format,
            MapEndpoints(document),
            warnings);
    }

    private static string FormatError(string? pointer, string message) =>
        string.IsNullOrEmpty(pointer) ? message : $"{pointer}: {message}";

    private static List<ApiEndpoint> MapEndpoints(OpenApiDocument document)
    {
        var endpoints = new List<ApiEndpoint>();
        foreach (var (path, item) in document.Paths)
        {
            foreach (var (operationType, operation) in item.Operations)
            {
                if (operationType == OperationType.Trace)
                {
                    continue;
                }

                var endpoint = new ApiEndpoint
                {
                    Method = operationType.ToString().ToUpperInvariant(),
                    Path = path,
                    OperationId = operation.OperationId,
                    Summary = Trim(operation.Summary),
                    Description = Trim(operation.Description),
                    Tag = operation.Tags?.FirstOrDefault()?.Name is { Length: > 0 } tag ? tag : "default",
                    Deprecated = operation.Deprecated,
                    Parameters = MapParameters(item.Parameters, operation.Parameters),
                    ResponseCodes = operation.Responses?.Keys.ToList() ?? [],
                };
                MapRequestBody(operation.RequestBody, endpoint);
                endpoints.Add(endpoint);
            }
        }
        return endpoints;
    }

    private static List<EndpointParameter> MapParameters(IList<OpenApiParameter>? pathLevel, IList<OpenApiParameter>? operationLevel)
    {
        // Operation-level parameters override path-level ones with the same name and location.
        var merged = new Dictionary<(string, ParameterLocation?), OpenApiParameter>();
        foreach (var p in (pathLevel ?? []).Concat(operationLevel ?? []))
        {
            merged[(p.Name, p.In)] = p;
        }

        return merged.Values
            .Where(p => p.In is not null)
            .Select(p => new EndpointParameter
            {
                Name = p.Name,
                In = p.In switch
                {
                    ParameterLocation.Path => "path",
                    ParameterLocation.Header => "header",
                    ParameterLocation.Cookie => "cookie",
                    _ => "query",
                },
                Required = p.Required || p.In == ParameterLocation.Path,
                Type = p.Schema?.Type is "array" && p.Schema.Items?.Type is { } itemType ? $"array<{itemType}>" : p.Schema?.Type,
                Format = p.Schema?.Format,
                Description = Trim(p.Description),
                Example = OpenApiAnyConverter.ToDisplayString(p.Example ?? p.Schema?.Example ?? p.Schema?.Default ?? p.Schema?.Items?.Default),
                Enum = (p.Schema?.Enum is { Count: > 0 } e ? e : p.Schema?.Items?.Enum is { Count: > 0 } ie ? ie : null)?
                    .Select(OpenApiAnyConverter.ToDisplayString).Where(v => v is not null).Select(v => v!).ToList(),
            })
            .OrderBy(p => p.In switch { "path" => 0, "query" => 1, "header" => 2, _ => 3 })
            .ToList();
    }

    private static void MapRequestBody(OpenApiRequestBody? body, ApiEndpoint endpoint)
    {
        if (body?.Content is not { Count: > 0 })
        {
            return;
        }

        var (contentType, media) = body.Content
            .OrderBy(c => c.Key.Equals("application/json", StringComparison.OrdinalIgnoreCase) ? 0
                : c.Key.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ? 1
                : c.Key.Contains("json", StringComparison.OrdinalIgnoreCase) ? 2 : 3)
            .First();
        endpoint.RequestBodyContentType = contentType;

        if (SchemaSummarizer.Summarize(media.Schema) is { } summary)
        {
            endpoint.RequestBodySchema = summary.ToJsonString(DisplayJson);
        }

        var sample = media.Example is not null
            ? OpenApiAnyConverter.ToNode(media.Example)
            : media.Examples?.Values.FirstOrDefault()?.Value is { } namedExample
                ? OpenApiAnyConverter.ToNode(namedExample)
                : SchemaSampleGenerator.Generate(media.Schema);
        if (sample is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            endpoint.SampleRequestBody = sample.ToJsonString(DisplayJson);
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
