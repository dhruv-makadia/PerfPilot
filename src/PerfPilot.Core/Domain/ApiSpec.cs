namespace PerfPilot.Core.Domain;

public enum SpecSource
{
    Url = 0,
    Upload = 1,
}

/// <summary>One imported version of an environment's OpenAPI document.</summary>
public class ApiSpec
{
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public TargetEnvironment? Environment { get; set; }

    /// <summary>Monotonic version number per environment (1, 2, 3...).</summary>
    public int Version { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }

    /// <summary>Document format, e.g. "OpenAPI 3.0.4" or "Swagger 2.0".</summary>
    public string SpecFormat { get; set; } = string.Empty;

    public SpecSource Source { get; set; }

    /// <summary>The URL fetched, or the uploaded file name.</summary>
    public string SourceLocation { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; }
    public string? ImportedBy { get; set; }
    public string RawContent { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public int EndpointCount { get; set; }

    public List<ApiEndpoint> Endpoints { get; set; } = [];
}

public class ApiEndpoint
{
    public Guid Id { get; set; }
    public Guid ApiSpecId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? OperationId { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Tag { get; set; } = "default";
    public bool Deprecated { get; set; }
    public List<EndpointParameter> Parameters { get; set; } = [];
    public string? RequestBodyContentType { get; set; }

    /// <summary>Simplified JSON rendering of the request body schema for display.</summary>
    public string? RequestBodySchema { get; set; }

    /// <summary>Sample JSON body generated from the schema.</summary>
    public string? SampleRequestBody { get; set; }

    public List<string> ResponseCodes { get; set; } = [];
    public int SortOrder { get; set; }
}

public class EndpointParameter
{
    public string Name { get; set; } = string.Empty;

    /// <summary>path, query, header or cookie.</summary>
    public string In { get; set; } = "query";

    public bool Required { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Description { get; set; }
    public string? Example { get; set; }
    public List<string>? Enum { get; set; }
}
