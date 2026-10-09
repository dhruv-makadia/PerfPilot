using Microsoft.Extensions.Logging.Abstractions;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Services;
using PerfPilot.Infrastructure.Http;

namespace PerfPilot.Tests;

public class HeaderRulesTests
{
    [Fact]
    public void Login_headers_default_to_json_content_type()
    {
        var headers = HeaderRules.BuildLoginHeaders(new Dictionary<string, string> { ["X-Tenant"] = "acme" });

        Assert.Equal(["Content-Type", "X-Tenant"], headers.Keys);
        Assert.Equal("application/json", headers["Content-Type"]);
    }

    [Fact]
    public void Configured_content_type_replaces_default_and_is_normalized()
    {
        var headers = HeaderRules.BuildLoginHeaders(new Dictionary<string, string> { ["content-type"] = "application/x-www-form-urlencoded" });

        Assert.Equal(["Content-Type"], headers.Keys);
        Assert.Equal("application/x-www-form-urlencoded", headers["Content-Type"]);
    }

    [Theory]
    [InlineData("X-Api-Key", true)]
    [InlineData("Ocp-Apim-Subscription-Key", true)]
    [InlineData("Bad Header", false)]
    [InlineData("", false)]
    public void Validates_header_names(string name, bool valid) => Assert.Equal(valid, HeaderRules.IsValidName(name));
}

public class LoginRequestTests
{
    private static ResolvedAuth Auth(Dictionary<string, string>? headers, string body = "{\"user\":\"a\"}") => new(
        AuthType.LoginFlow, null, null, "/token", body, "$.token", "Authorization", "Bearer ", LoginScope.PerVu, headers);

    [Fact]
    public async Task Sends_configured_headers_and_default_json_body()
    {
        using var request = LoginClient.BuildRequest("https://idp.local/token",
            Auth(new Dictionary<string, string> { ["X-Tenant-Id"] = "acme", ["Authorization"] = "Basic Y2xpZW50OnNlY3JldA==" }));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("acme", request.Headers.GetValues("X-Tenant-Id").Single());
        Assert.Equal("Basic Y2xpZW50OnNlY3JldA==", request.Headers.Authorization!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"user\":\"a\"}", await request.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Supports_form_urlencoded_and_overriding_accept()
    {
        using var request = LoginClient.BuildRequest("https://idp.local/token", Auth(
            new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded", ["Accept"] = "*/*" },
            "grant_type=client_credentials&scope=api"));

        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("*/*", Assert.Single(request.Headers.Accept).ToString());
    }
}

public class EnvironmentLoginHeadersTests
{
    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string protectedValue) => protectedValue["enc:".Length..];
    }

    private sealed class InMemoryEnvironments : IEnvironmentRepository
    {
        public readonly Dictionary<Guid, TargetEnvironment> Items = [];
        public Task<List<TargetEnvironment>> ListAsync(CancellationToken ct) => Task.FromResult(Items.Values.ToList());
        public Task<TargetEnvironment?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct) =>
            Task.FromResult(Items.Values.Any(e => e.Name == name && e.Id != excludeId));
        public Task<int> CountScenariosAsync(Guid environmentId, CancellationToken ct) => Task.FromResult(0);
        public Task AddAsync(TargetEnvironment environment, CancellationToken ct) { Items[environment.Id] = environment; return Task.CompletedTask; }
        public Task UpdateAsync(TargetEnvironment environment, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(TargetEnvironment environment, CancellationToken ct) { Items.Remove(environment.Id); return Task.CompletedTask; }
    }

    private sealed class NoSpecs : ISpecRepository
    {
        public Task<List<ApiSpec>> ListVersionsAsync(Guid environmentId, CancellationToken ct) => Task.FromResult(new List<ApiSpec>());
        public Task<ApiSpec?> GetLatestSummaryAsync(Guid environmentId, CancellationToken ct) => Task.FromResult<ApiSpec?>(null);
        public Task<Dictionary<Guid, ApiSpec>> GetLatestSummariesAsync(IEnumerable<Guid> environmentIds, CancellationToken ct) => Task.FromResult(new Dictionary<Guid, ApiSpec>());
        public Task<ApiSpec?> GetWithEndpointsAsync(Guid specId, CancellationToken ct) => Task.FromResult<ApiSpec?>(null);
        public Task<string?> GetRawContentAsync(Guid specId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<int> GetNextVersionAsync(Guid environmentId, CancellationToken ct) => Task.FromResult(1);
        public Task AddAsync(ApiSpec spec, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingTester : IConnectionTester
    {
        public ConnectionTestContext? Last;
        public Task<ConnectionTestResult> TestAsync(ConnectionTestContext context, CancellationToken ct)
        {
            Last = context;
            return Task.FromResult(new ConnectionTestResult(true, []));
        }
    }

    private readonly InMemoryEnvironments _repo = new();
    private readonly CapturingTester _tester = new();
    private EnvironmentService Service() =>
        new(_repo, new NoSpecs(), new FakeProtector(), _tester, TimeProvider.System, NullLogger<EnvironmentService>.Instance);

    private static SaveEnvironmentRequest Request(IReadOnlyList<HeaderInput>? headers, string? body = "{\"u\":\"a\"}") => new(
        "Login env", "https://api.local", null, false, false,
        new SaveAuthConfig(AuthType.LoginFlow, null, false, null, "/auth/token", body, "$.token", null, null, LoginScope.PerVu, headers));

    [Fact]
    public async Task Stores_header_values_encrypted_and_returns_only_names()
    {
        var dto = await Service().CreateAsync(Request([new("X-Tenant-Id", "acme"), new("Ocp-Apim-Subscription-Key", "s3cret")]), default);

        Assert.Equal(["X-Tenant-Id", "Ocp-Apim-Subscription-Key"], dto.Auth.LoginHeaderNames);
        var stored = _repo.Items[dto.Id].LoginHeaders;
        Assert.Equal("enc:s3cret", stored[1].ProtectedValue);

        var resolved = Service().ResolveAuth(_repo.Items[dto.Id]);
        Assert.Equal("s3cret", resolved.LoginHeaders!["Ocp-Apim-Subscription-Key"]);
    }

    [Fact]
    public async Task Blank_value_keeps_stored_value_and_omitted_header_is_removed()
    {
        var created = await Service().CreateAsync(Request([new("X-Tenant-Id", "acme"), new("X-Client-Secret", "old")]), default);

        await Service().UpdateAsync(created.Id, Request([new("x-client-secret", ""), new("X-Region", "eu")], body: null), default);

        var stored = _repo.Items[created.Id];
        Assert.Equal(["x-client-secret", "X-Region"], stored.LoginHeaders.Select(h => h.Name));
        Assert.Equal("enc:old", stored.LoginHeaders[0].ProtectedValue);
        Assert.Equal("enc:{\"u\":\"a\"}", stored.ProtectedLoginBody);
    }

    [Fact]
    public async Task Null_header_list_keeps_existing_headers()
    {
        var created = await Service().CreateAsync(Request([new("X-Tenant-Id", "acme")]), default);

        await Service().UpdateAsync(created.Id, Request(null, body: null), default);

        Assert.Equal("enc:acme", Assert.Single(_repo.Items[created.Id].LoginHeaders).ProtectedValue);
    }

    [Fact]
    public async Task Rejects_new_header_without_value_duplicates_and_reserved_headers()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(
            Request([new("X-New", ""), new("X-Dup", "1"), new("x-dup", "2"), new("Host", "evil"), new("bad name", "v")]), default));

        var messages = string.Join(" | ", ex.Errors["auth.loginHeaders"]);
        Assert.Contains("Enter a value for login header 'X-New'", messages);
        Assert.Contains("listed more than once", messages);
        Assert.Contains("'Host' header is set automatically", messages);
        Assert.Contains("'bad name' is not a valid header name", messages);
    }

    [Fact]
    public async Task Form_content_type_allows_non_json_body()
    {
        var dto = await Service().CreateAsync(
            Request([new("Content-Type", "application/x-www-form-urlencoded")], body: "grant_type=client_credentials&client_id=perf"), default);

        Assert.Equal("enc:grant_type=client_credentials&client_id=perf", _repo.Items[dto.Id].ProtectedLoginBody);
    }

    [Fact]
    public async Task Json_body_is_still_validated_without_content_type_header()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Request([], body: "user=a"), default));
        Assert.Contains("must be valid JSON", ex.Errors["auth.loginBody"][0]);
    }

    [Fact]
    public async Task Test_connection_uses_stored_values_for_blank_header_fields()
    {
        var created = await Service().CreateAsync(Request([new("X-Client-Secret", "kept")]), default);

        await Service().TestConnectionAsync(new TestConnectionRequest(created.Id, Request([new("X-Client-Secret", null)], body: null)), default);

        Assert.Equal("kept", _tester.Last!.Auth.LoginHeaders!["X-Client-Secret"]);
    }

    [Fact]
    public async Task Switching_away_from_login_flow_clears_headers()
    {
        var created = await Service().CreateAsync(Request([new("X-Tenant-Id", "acme")]), default);

        await Service().UpdateAsync(created.Id, new SaveEnvironmentRequest("Login env", "https://api.local", null, false, false,
            new SaveAuthConfig(AuthType.None, null, false, null, null, null, null, null, null, LoginScope.PerVu)), default);

        Assert.Empty(_repo.Items[created.Id].LoginHeaders);
    }
}
