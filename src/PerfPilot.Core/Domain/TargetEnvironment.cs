namespace PerfPilot.Core.Domain;

public enum AuthType
{
    None = 0,
    BearerToken = 1,
    ApiKey = 2,
    LoginFlow = 3,
}

/// <summary>Where the login flow runs: once per virtual user, or once in k6 setup() and shared.</summary>
public enum LoginScope
{
    PerVu = 0,
    Shared = 1,
}

/// <summary>A target system QA engineers can load test.</summary>
public class TargetEnvironment
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? OpenApiUrl { get; set; }
    public bool IsProduction { get; set; }

    /// <summary>Skip TLS certificate validation for server-side calls and k6 (internal staging certs).</summary>
    public bool IgnoreTlsErrors { get; set; }

    public AuthType AuthType { get; set; }

    /// <summary>Encrypted bearer token or API key value (Data Protection payload).</summary>
    public string? ProtectedSecret { get; set; }

    public string? ApiKeyHeaderName { get; set; }

    /// <summary>Login endpoint; absolute URL or a path relative to <see cref="BaseUrl"/>.</summary>
    public string? LoginUrl { get; set; }

    /// <summary>Encrypted JSON body posted to the login endpoint (usually contains credentials).</summary>
    public string? ProtectedLoginBody { get; set; }

    /// <summary>Extra headers sent with the login request. Values are encrypted individually; names stay readable for the UI.</summary>
    public List<StoredHeader> LoginHeaders { get; set; } = [];

    public string? LoginTokenJsonPath { get; set; }
    public string LoginTokenHeaderName { get; set; } = "Authorization";
    public string LoginTokenPrefix { get; set; } = "Bearer ";
    public LoginScope LoginScope { get; set; } = LoginScope.PerVu;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ApiSpec> Specs { get; set; } = [];
}

/// <summary>An HTTP header whose value is stored encrypted (Data Protection payload).</summary>
public class StoredHeader
{
    public string Name { get; set; } = string.Empty;
    public string ProtectedValue { get; set; } = string.Empty;
}
