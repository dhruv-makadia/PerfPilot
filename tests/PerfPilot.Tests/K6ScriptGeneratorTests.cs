using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;

namespace PerfPilot.Tests;

public class K6ScriptGeneratorTests
{
    private readonly K6ScriptGenerator _generator = new();

    internal static ScenarioDefinition SampleDefinition() => new()
    {
        CsvData = "petId,status\n1,available\n2,\"sold, out\"\n",
        LoadProfile = new LoadProfile
        {
            Preset = LoadPreset.Load,
            Stages =
            [
                new LoadStage { Duration = "30s", Target = 10 },
                new LoadStage { Duration = "1m", Target = 10 },
                new LoadStage { Duration = "15s", Target = 0 },
            ],
            ThinkTimeMinSeconds = 0.5,
            ThinkTimeMaxSeconds = 1.5,
        },
        GlobalThresholds = new SlaThresholds { P95Ms = 800, MaxErrorRatePercent = 1, MinRps = 5 },
        Endpoints =
        [
            new ScenarioEndpoint
            {
                Method = "GET",
                Path = "/pet/{petId}",
                Weight = 70,
                Parameters =
                [
                    new ParameterValue { Name = "petId", In = ParameterLocation.Path, Mode = ParameterValueMode.CsvColumn, CsvColumn = "petId" },
                    new ParameterValue { Name = "X-Trace", In = ParameterLocation.Header, Value = "{{random.guid}}" },
                ],
                ExpectedStatusCodes = [200, 404],
                Thresholds = new SlaThresholds { P95Ms = 300, P99Ms = 600 },
            },
            new ScenarioEndpoint
            {
                Name = "Create pet",
                Method = "POST",
                Path = "/pet",
                Weight = 30,
                Parameters = [new ParameterValue { Name = "source", In = ParameterLocation.Query, Value = "perf's test" }],
                Body = "{\n  \"id\": {{random.int}},\n  \"name\": \"{{csv.status}}\",\n  \"owner\": \"{{random.email}}\"\n}",
                ContentType = "application/json",
                ExpectedStatusCodes = [200],
                Thresholds = new SlaThresholds { MaxErrorRatePercent = 0.5, MinRps = 1.5 },
            },
        ],
    };

    private static K6EnvironmentInfo Env(AuthType auth, LoginScope scope = LoginScope.PerVu, string? loginUrl = null) => new(
        "Staging",
        "https://petstore.staging.local/api/v3/",
        IgnoreTlsErrors: auth == AuthType.LoginFlow,
        auth,
        ApiKeyHeaderName: auth == AuthType.ApiKey ? "X-API-Key" : null,
        LoginUrl: loginUrl,
        LoginTokenJsonPath: auth == AuthType.LoginFlow ? "$.data.access_token" : null,
        LoginTokenHeaderName: "Authorization",
        LoginTokenPrefix: "Bearer ",
        scope);

    [Fact]
    public void Generates_bearer_script_with_csv_thresholds_and_weighted_endpoints()
    {
        var script = _generator.Generate(new K6ScriptInput("Pet store load", Env(AuthType.BearerToken), SampleDefinition()));
        Snapshot.Match(script.Content, "bearer_csv_thresholds");
    }

    [Fact]
    public void Generates_per_vu_login_flow_with_relative_login_url()
    {
        var def = SampleDefinition();
        def.CsvData = null;
        def.Endpoints[0].Parameters[0] = new ParameterValue { Name = "petId", In = ParameterLocation.Path, Value = "{{random.int}}" };
        def.Endpoints[1].Body = "{\"name\":\"rex\"}";
        def.LoadProfile = new LoadProfile { Preset = LoadPreset.Smoke, Stages = [new LoadStage { Duration = "1m", Target = 1 }], ThinkTimeMinSeconds = 1, ThinkTimeMaxSeconds = 2 };
        def.GlobalThresholds = new SlaThresholds();

        var script = _generator.Generate(new K6ScriptInput("Login smoke", Env(AuthType.LoginFlow, LoginScope.PerVu, "auth/login"), def));
        Snapshot.Match(script.Content, "login_per_vu");
    }

    [Fact]
    public void Shared_login_uses_setup_and_absolute_url()
    {
        var script = _generator.Generate(new K6ScriptInput("Shared", Env(AuthType.LoginFlow, LoginScope.Shared, "https://idp.local/token"), SampleDefinition()));

        Assert.Contains("const LOGIN_URL = 'https://idp.local/token';", script.Content);
        Assert.Contains("export function setup() {\n  return { token: login() };\n}", script.Content);
        Assert.Contains("headers[LOGIN_TOKEN_HEADER] = LOGIN_TOKEN_PREFIX + data.token;", script.Content);
        Assert.Contains("const LOGIN_HEADERS = JSON.parse(__ENV.PERFPILOT_LOGIN_HEADERS || '{\"Content-Type\":\"application/json\"}');", script.Content);
        Assert.Contains("    headers: LOGIN_HEADERS,\n", script.Content);
        Assert.DoesNotContain("let vuToken", script.Content);
        Assert.Contains("insecureSkipTLSVerify: true", script.Content);
        Assert.Equal([K6EnvVars.LoginBody], script.RequiredEnvironmentVariables);
    }

    [Fact]
    public void Api_key_auth_reads_key_from_environment_variable()
    {
        var script = _generator.Generate(new K6ScriptInput("Key", Env(AuthType.ApiKey), SampleDefinition()));

        Assert.Contains("const API_KEY_HEADER = 'X-API-Key';", script.Content);
        Assert.Contains("const API_KEY = __ENV.PERFPILOT_API_KEY;", script.Content);
        Assert.Contains("// Run it yourself:\n//   k6 run -e PERFPILOT_API_KEY=<value> script.js", script.Content);
    }

    [Fact]
    public void No_auth_has_empty_apply_auth_and_no_env_requirements()
    {
        var script = _generator.Generate(new K6ScriptInput("Open", Env(AuthType.None), SampleDefinition()));

        Assert.Contains("// No authentication configured for this environment.", script.Content);
        Assert.Empty(script.RequiredEnvironmentVariables);
        Assert.DoesNotContain("__ENV.PERFPILOT_BEARER_TOKEN", script.Content);
    }

    [Fact]
    public void Builds_threshold_plan_for_global_and_endpoint_slas()
    {
        var script = _generator.Generate(new K6ScriptInput("Plan", Env(AuthType.None), SampleDefinition()));

        Assert.Equal(
        [
            "Global|P95|http_req_duration{kind:test}|p(95)<=800",
            "Global|ErrorRate|http_req_failed{kind:test}|rate<=0.01",
            "Global|MinRps|http_reqs{kind:test}|rate>=5",
            "GET /pet/{petId}|P95|http_req_duration{endpoint:ep1}|p(95)<=300",
            "GET /pet/{petId}|P99|http_req_duration{endpoint:ep1}|p(99)<=600",
            "Create pet|ErrorRate|http_req_failed{endpoint:ep2}|rate<=0.005",
            "Create pet|MinRps|http_reqs{endpoint:ep2}|rate>=1.5",
        ], script.Thresholds.Select(t => $"{t.ScopeName}|{t.Metric}|{t.K6Metric}|{t.Expression}"));
        Assert.Equal("ep2", script.Thresholds[^1].EndpointKey);
        Assert.Null(script.Thresholds[0].EndpointKey);
    }

    [Fact]
    public void Always_declares_submetrics_so_summary_contains_per_endpoint_results()
    {
        var def = SampleDefinition();
        def.GlobalThresholds = new SlaThresholds();
        foreach (var ep in def.Endpoints)
        {
            ep.Thresholds = new SlaThresholds();
        }

        var script = _generator.Generate(new K6ScriptInput("No SLA", Env(AuthType.None), def));

        Assert.Empty(script.Thresholds);
        Assert.Contains("'http_req_duration{endpoint:ep2}': ['max>=0'],", script.Content);
        Assert.Contains("'http_req_failed{endpoint:ep2}': ['rate>=0'],", script.Content);
        Assert.Contains("'http_reqs{endpoint:ep2}': ['count>=0'],", script.Content);
        Assert.Contains("'http_req_duration{kind:test}': ['max>=0'],", script.Content);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var input = new K6ScriptInput("Same", Env(AuthType.BearerToken), SampleDefinition());
        Assert.Equal(_generator.Generate(input).Content, _generator.Generate(input).Content);
    }

    [Fact]
    public void Describes_endpoints_with_stable_keys_and_check_names()
    {
        var endpoints = K6ScriptGenerator.DescribeEndpoints(SampleDefinition());

        Assert.Equal(["ep1", "ep2"], endpoints.Select(e => e.Key));
        Assert.Equal("ep1 GET /pet/{petId} status in [200,404]", endpoints[0].CheckName);
        Assert.Equal("Create pet", endpoints[1].Name);
    }
}
