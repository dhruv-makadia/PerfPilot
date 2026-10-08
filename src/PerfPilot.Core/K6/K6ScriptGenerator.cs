using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Services;

namespace PerfPilot.Core.K6;

/// <summary>
/// Turns a scenario into a self-contained k6 script. Output is deterministic (no timestamps, stable keys)
/// so it can be snapshot-tested, and never contains secrets: those are read from environment variables.
/// </summary>
public sealed class K6ScriptGenerator : IK6ScriptGenerator
{
    /// <summary>Tag value on every scenario request; login requests use kind:auth and are excluded from SLAs.</summary>
    public const string TestKindTag = "kind:test";

    public static readonly string[] SummaryTrendStats = ["avg", "min", "med", "max", "p(90)", "p(95)", "p(99)", "count"];

    private static readonly JsonSerializerOptions ScriptJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string EndpointKey(int index) => $"ep{index + 1}";

    public static string DurationMetric(string? endpointKey) => $"http_req_duration{{{Selector(endpointKey)}}}";
    public static string FailedMetric(string? endpointKey) => $"http_req_failed{{{Selector(endpointKey)}}}";
    public static string RequestsMetric(string? endpointKey) => $"http_reqs{{{Selector(endpointKey)}}}";

    private static string Selector(string? endpointKey) => endpointKey is null ? TestKindTag : $"endpoint:{endpointKey}";

    public K6Script Generate(K6ScriptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var def = input.Definition;
        var env = input.Environment;

        var endpoints = DescribeEndpoints(def);

        var thresholdPlan = new List<ThresholdDefinition>();
        var thresholds = new List<KeyValuePair<string, List<string>>>();
        AddThresholds(thresholds, thresholdPlan, null, "Global", def.GlobalThresholds);
        for (var i = 0; i < def.Endpoints.Count; i++)
        {
            AddThresholds(thresholds, thresholdPlan, endpoints[i].Key, endpoints[i].Name, def.Endpoints[i].Thresholds);
        }

        var requiredEnv = RequiredEnvVars(env.AuthType);
        var csv = CsvParser.Parse(def.CsvData);

        var sb = new StringBuilder();
        WriteHeader(sb, input, requiredEnv);
        WriteImports(sb, csv.Rows.Count > 0);
        sb.Append("const BASE_URL = (__ENV.").Append(K6EnvVars.BaseUrl).Append(" || ").Append(Js(UrlHelper.NormalizeBaseUrl(env.BaseUrl))).Append(").replace(/\\/+$/, '');\n\n");
        WriteOptions(sb, def.LoadProfile, thresholds, env.IgnoreTlsErrors);
        WriteCsvData(sb, csv);
        WriteEndpoints(sb, def, endpoints);
        WriteThinkTime(sb, def.LoadProfile);
        WriteAuth(sb, env);
        WriteHelpers(sb);
        WriteDefaultFunction(sb);

        return new K6Script(sb.ToString(), thresholdPlan, endpoints, requiredEnv);
    }

    /// <summary>Keys, names and check names of a definition's endpoints, as they appear in the script and its metrics.</summary>
    public static IReadOnlyList<K6EndpointInfo> DescribeEndpoints(ScenarioDefinition def) => def.Endpoints
        .Select((ep, i) => new K6EndpointInfo(
            EndpointKey(i),
            ep.DisplayName,
            ep.Method.ToUpperInvariant(),
            ep.Path,
            $"{EndpointKey(i)} {ep.DisplayName} status in [{string.Join(",", ep.ExpectedStatusCodes.Distinct())}]"))
        .ToList();

    public static IReadOnlyList<string> RequiredEnvVars(AuthType authType) => authType switch
    {
        AuthType.BearerToken => [K6EnvVars.BearerToken],
        AuthType.ApiKey => [K6EnvVars.ApiKey],
        AuthType.LoginFlow => [K6EnvVars.LoginBody],
        _ => [],
    };

    private static void AddThresholds(
        List<KeyValuePair<string, List<string>>> thresholds,
        List<ThresholdDefinition> plan,
        string? endpointKey,
        string scopeName,
        SlaThresholds? sla)
    {
        sla ??= new SlaThresholds();
        var duration = new List<string>();
        var failed = new List<string>();
        var requests = new List<string>();

        void Add(List<string> target, string metric, SlaMetric kind, double limit, string expression)
        {
            target.Add(expression);
            plan.Add(new ThresholdDefinition
            {
                EndpointKey = endpointKey,
                ScopeName = scopeName,
                Metric = kind,
                Limit = limit,
                K6Metric = metric,
                Expression = expression,
            });
        }

        if (sla.P95Ms is { } p95)
        {
            Add(duration, DurationMetric(endpointKey), SlaMetric.P95, p95, $"p(95)<={Num(p95)}");
        }
        if (sla.P99Ms is { } p99)
        {
            Add(duration, DurationMetric(endpointKey), SlaMetric.P99, p99, $"p(99)<={Num(p99)}");
        }
        if (sla.MaxErrorRatePercent is { } err)
        {
            Add(failed, FailedMetric(endpointKey), SlaMetric.ErrorRate, err, $"rate<={Num(err / 100)}");
        }
        if (sla.MinRps is { } rps)
        {
            Add(requests, RequestsMetric(endpointKey), SlaMetric.MinRps, rps, $"rate>={Num(rps)}");
        }

        // Always declare the submetrics (with always-true expressions when no SLA is set) so that
        // k6 includes them in the summary export; per-endpoint results are read from there.
        thresholds.Add(new(DurationMetric(endpointKey), duration.Count > 0 ? duration : ["max>=0"]));
        thresholds.Add(new(FailedMetric(endpointKey), failed.Count > 0 ? failed : ["rate>=0"]));
        thresholds.Add(new(RequestsMetric(endpointKey), requests.Count > 0 ? requests : ["count>=0"]));
    }

    private static void WriteHeader(StringBuilder sb, K6ScriptInput input, IReadOnlyList<string> requiredEnv)
    {
        var profile = input.Definition.LoadProfile;
        var total = LoadProfilePresets.TotalDuration(profile.Stages);
        sb.Append("// =============================================================================\n");
        sb.Append("// PerfPilot generated k6 script\n");
        sb.Append("// Scenario:    ").Append(OneLine(input.ScenarioName)).Append('\n');
        sb.Append("// Environment: ").Append(OneLine(input.Environment.Name)).Append(" (").Append(OneLine(input.Environment.BaseUrl)).Append(")\n");
        sb.Append("// Load:        ").Append(profile.Preset).Append(" profile, max ").Append(LoadProfilePresets.MaxTarget(profile.Stages))
            .Append(" VUs, ").Append(DurationParser.Format(total)).Append(" total\n");
        sb.Append("//\n");
        if (requiredEnv.Count > 0)
        {
            sb.Append("// Secrets are never embedded in this file. Provide them as environment variables:\n");
            foreach (var name in requiredEnv)
            {
                sb.Append("//   ").Append(name.PadRight(24)).Append(EnvVarDescription(name)).Append('\n');
            }
        }
        else
        {
            sb.Append("// This environment uses no authentication.\n");
        }
        sb.Append("// Optional: ").Append(K6EnvVars.BaseUrl).Append(" overrides the target base URL.\n");
        sb.Append("//\n");
        sb.Append("// Run it yourself:\n");
        sb.Append("//   k6 run");
        foreach (var name in requiredEnv)
        {
            sb.Append(" -e ").Append(name).Append("=<value>");
        }
        sb.Append(" script.js\n");
        sb.Append("// =============================================================================\n");
    }

    private static string EnvVarDescription(string name) => name switch
    {
        K6EnvVars.BearerToken => "Static bearer token",
        K6EnvVars.ApiKey => "API key value",
        K6EnvVars.LoginBody => "JSON body posted to the login endpoint",
        _ => string.Empty,
    };

    private static void WriteImports(StringBuilder sb, bool hasCsv)
    {
        sb.Append("import http from 'k6/http';\n");
        sb.Append("import { check, sleep } from 'k6';\n");
        if (hasCsv)
        {
            sb.Append("import { SharedArray } from 'k6/data';\n");
        }
        sb.Append('\n');
    }

    private static void WriteOptions(StringBuilder sb, LoadProfile profile, List<KeyValuePair<string, List<string>>> thresholds, bool ignoreTls)
    {
        sb.Append("export const options = {\n");
        sb.Append("  stages: [\n");
        foreach (var stage in profile.Stages)
        {
            sb.Append("    { duration: ").Append(Js(stage.Duration.Trim())).Append(", target: ").Append(stage.Target.ToString(CultureInfo.InvariantCulture)).Append(" },\n");
        }
        sb.Append("  ],\n");
        sb.Append("  thresholds: {\n");
        foreach (var (metric, expressions) in thresholds)
        {
            sb.Append("    ").Append(Js(metric)).Append(": [").Append(string.Join(", ", expressions.Select(Js))).Append("],\n");
        }
        sb.Append("  },\n");
        sb.Append("  summaryTrendStats: [").Append(string.Join(", ", SummaryTrendStats.Select(Js))).Append("],\n");
        sb.Append("  insecureSkipTLSVerify: ").Append(ignoreTls ? "true" : "false").Append(",\n");
        sb.Append("  discardResponseBodies: true,\n");
        sb.Append("  userAgent: 'PerfPilot/1.0 (k6)',\n");
        sb.Append("};\n\n");
    }

    private static void WriteCsvData(StringBuilder sb, CsvTable csv)
    {
        if (csv.Rows.Count == 0)
        {
            sb.Append("// No CSV test data configured.\n");
            sb.Append("const CSV_DATA = [];\n\n");
            return;
        }

        sb.Append("// CSV test data (").Append(csv.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" rows), shared read-only across VUs.\n");
        sb.Append("const CSV_DATA = new SharedArray('perfpilot-csv', function () {\n");
        sb.Append("  return [\n");
        foreach (var row in csv.Rows)
        {
            var ordered = csv.Columns.Where(row.ContainsKey).Distinct().ToDictionary(c => c, c => row[c]);
            sb.Append("    ").Append(JsonSerializer.Serialize(ordered, CompactJson)).Append(",\n");
        }
        sb.Append("  ];\n");
        sb.Append("});\n\n");
    }

    private static void WriteEndpoints(StringBuilder sb, ScenarioDefinition def, IReadOnlyList<K6EndpointInfo> endpoints)
    {
        var model = def.Endpoints.Select((ep, i) => new
        {
            key = endpoints[i].Key,
            name = endpoints[i].Name,
            method = endpoints[i].Method,
            path = ep.Path,
            weight = ep.Weight,
            @params = ep.Parameters.Select(p => new
            {
                name = p.Name,
                @in = p.In switch
                {
                    ParameterLocation.Path => "path",
                    ParameterLocation.Header => "header",
                    _ => "query",
                },
                mode = p.Mode == ParameterValueMode.CsvColumn ? "csv" : "static",
                value = p.Mode == ParameterValueMode.CsvColumn ? null : p.Value ?? string.Empty,
                column = p.Mode == ParameterValueMode.CsvColumn ? p.CsvColumn : null,
            }).ToList(),
            body = string.IsNullOrWhiteSpace(ep.Body) ? null : ep.Body,
            contentType = string.IsNullOrWhiteSpace(ep.Body) ? null : (string.IsNullOrWhiteSpace(ep.ContentType) ? "application/json" : ep.ContentType.Trim()),
            expected = ep.ExpectedStatusCodes.Distinct().ToList(),
            check = endpoints[i].CheckName,
        }).ToList();

        sb.Append("// Endpoints with traffic weight (%). One is picked at random per iteration.\n");
        sb.Append("const ENDPOINTS = ").Append(JsonSerializer.Serialize(model, ScriptJson)).Append(";\n");
        sb.Append("for (const ep of ENDPOINTS) {\n");
        sb.Append("  // Requests with an unexpected status count as failed (http_req_failed).\n");
        sb.Append("  ep.responseCallback = http.expectedStatuses(...ep.expected);\n");
        sb.Append("}\n");
        sb.Append("const TOTAL_WEIGHT = ENDPOINTS.reduce(function (sum, ep) { return sum + ep.weight; }, 0);\n\n");
    }

    private static void WriteThinkTime(StringBuilder sb, LoadProfile profile)
    {
        sb.Append("const THINK_TIME_MIN = ").Append(Num(profile.ThinkTimeMinSeconds)).Append(";\n");
        sb.Append("const THINK_TIME_MAX = ").Append(Num(profile.ThinkTimeMaxSeconds)).Append(";\n\n");
    }

    private static void WriteAuth(StringBuilder sb, K6EnvironmentInfo env)
    {
        sb.Append("// ---- Authentication ----------------------------------------------------------\n");
        switch (env.AuthType)
        {
            case AuthType.BearerToken:
                sb.Append("const BEARER_TOKEN = __ENV.").Append(K6EnvVars.BearerToken).Append(";\n");
                sb.Append("if (!BEARER_TOKEN) {\n");
                sb.Append("  throw new Error('Environment variable ").Append(K6EnvVars.BearerToken).Append(" is required (static bearer token).');\n");
                sb.Append("}\n\n");
                sb.Append("function applyAuth(headers, data) {\n");
                sb.Append("  headers['Authorization'] = 'Bearer ' + BEARER_TOKEN;\n");
                sb.Append("}\n\n");
                break;

            case AuthType.ApiKey:
                sb.Append("const API_KEY_HEADER = ").Append(Js(env.ApiKeyHeaderName ?? "X-API-Key")).Append(";\n");
                sb.Append("const API_KEY = __ENV.").Append(K6EnvVars.ApiKey).Append(";\n");
                sb.Append("if (!API_KEY) {\n");
                sb.Append("  throw new Error('Environment variable ").Append(K6EnvVars.ApiKey).Append(" is required (API key value).');\n");
                sb.Append("}\n\n");
                sb.Append("function applyAuth(headers, data) {\n");
                sb.Append("  headers[API_KEY_HEADER] = API_KEY;\n");
                sb.Append("}\n\n");
                break;

            case AuthType.LoginFlow:
                WriteLoginAuth(sb, env);
                break;

            default:
                sb.Append("function applyAuth(headers, data) {\n");
                sb.Append("  // No authentication configured for this environment.\n");
                sb.Append("}\n\n");
                break;
        }
    }

    private static void WriteLoginAuth(StringBuilder sb, K6EnvironmentInfo env)
    {
        var loginUrl = env.LoginUrl ?? "/";
        sb.Append("const LOGIN_URL = ")
            .Append(UrlHelper.IsAbsoluteHttpUrl(loginUrl) ? Js(loginUrl.Trim()) : "BASE_URL + " + Js("/" + loginUrl.Trim().TrimStart('/')))
            .Append(";\n");
        sb.Append("const LOGIN_TOKEN_PATH = ").Append(Js(env.LoginTokenJsonPath ?? "$.token")).Append(";\n");
        sb.Append("const LOGIN_TOKEN_HEADER = ").Append(Js(env.LoginTokenHeaderName)).Append(";\n");
        sb.Append("const LOGIN_TOKEN_PREFIX = ").Append(Js(env.LoginTokenPrefix)).Append(";\n");
        sb.Append("const LOGIN_BODY = __ENV.").Append(K6EnvVars.LoginBody).Append(";\n");
        sb.Append("if (!LOGIN_BODY) {\n");
        sb.Append("  throw new Error('Environment variable ").Append(K6EnvVars.LoginBody).Append(" is required (JSON body for the login request).');\n");
        sb.Append("}\n\n");
        sb.Append("// Supports $.a.b, $.items[0].token and $['key'] style paths.\n");
        sb.Append("function extractJsonPath(obj, path) {\n");
        sb.Append("  let p = String(path).trim();\n");
        sb.Append("  if (p.charAt(0) === '$') { p = p.substring(1); }\n");
        sb.Append("  const re = /\\[(\\d+)\\]|\\['([^']+)'\\]|\\[\"([^\"]+)\"\\]|\\.?([^.\\[\\]]+)/g;\n");
        sb.Append("  let current = obj;\n");
        sb.Append("  let m;\n");
        sb.Append("  while ((m = re.exec(p)) !== null) {\n");
        sb.Append("    if (current === null || current === undefined) { return undefined; }\n");
        sb.Append("    const key = m[1] !== undefined ? Number(m[1]) : (m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : m[4]));\n");
        sb.Append("    current = current[key];\n");
        sb.Append("  }\n");
        sb.Append("  return current;\n");
        sb.Append("}\n\n");
        sb.Append("function login() {\n");
        sb.Append("  const res = http.post(LOGIN_URL, LOGIN_BODY, {\n");
        sb.Append("    headers: { 'Content-Type': 'application/json' },\n");
        sb.Append("    tags: { kind: 'auth', name: 'auth: login' },\n");
        sb.Append("    responseType: 'text',\n");
        sb.Append("  });\n");
        sb.Append("  if (res.status < 200 || res.status >= 300) {\n");
        sb.Append("    throw new Error('Login failed: HTTP ' + res.status + ' from ' + LOGIN_URL);\n");
        sb.Append("  }\n");
        sb.Append("  let payload;\n");
        sb.Append("  try {\n");
        sb.Append("    payload = JSON.parse(res.body);\n");
        sb.Append("  } catch (e) {\n");
        sb.Append("    throw new Error('Login response from ' + LOGIN_URL + ' is not valid JSON');\n");
        sb.Append("  }\n");
        sb.Append("  const token = extractJsonPath(payload, LOGIN_TOKEN_PATH);\n");
        sb.Append("  if (token === undefined || token === null || token === '') {\n");
        sb.Append("    throw new Error('Login succeeded but no token was found at ' + LOGIN_TOKEN_PATH);\n");
        sb.Append("  }\n");
        sb.Append("  return String(token);\n");
        sb.Append("}\n\n");

        if (env.LoginScope == LoginScope.Shared)
        {
            sb.Append("// Log in once before the test starts; all VUs share the token.\n");
            sb.Append("export function setup() {\n");
            sb.Append("  return { token: login() };\n");
            sb.Append("}\n\n");
            sb.Append("function applyAuth(headers, data) {\n");
            sb.Append("  headers[LOGIN_TOKEN_HEADER] = LOGIN_TOKEN_PREFIX + data.token;\n");
            sb.Append("}\n\n");
        }
        else
        {
            sb.Append("// Each VU logs in once, on its first iteration, and reuses its own token.\n");
            sb.Append("let vuToken = null;\n\n");
            sb.Append("function applyAuth(headers, data) {\n");
            sb.Append("  if (vuToken === null) {\n");
            sb.Append("    vuToken = login();\n");
            sb.Append("  }\n");
            sb.Append("  headers[LOGIN_TOKEN_HEADER] = LOGIN_TOKEN_PREFIX + vuToken;\n");
            sb.Append("}\n\n");
        }
    }

    private static void WriteHelpers(StringBuilder sb)
    {
        sb.Append(Lf("""
// ---- Helpers -------------------------------------------------------------------
function pickEndpoint() {
  let r = Math.random() * TOTAL_WEIGHT;
  for (let i = 0; i < ENDPOINTS.length; i++) {
    r -= ENDPOINTS[i].weight;
    if (r < 0) { return ENDPOINTS[i]; }
  }
  return ENDPOINTS[ENDPOINTS.length - 1];
}

function randomInt() {
  return Math.floor(Math.random() * 1000000) + 1;
}

function uuidv4() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
    const r = Math.random() * 16 | 0;
    const v = c === 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

function randomEmail() {
  return 'perfpilot.' + uuidv4().substring(0, 12) + '@example.com';
}

// Replaces {{random.int}}, {{random.guid}}, {{random.email}} and {{csv.column}} placeholders.
function fill(template, row) {
  if (template === null || template === undefined) { return template; }
  return String(template).replace(/\{\{\s*([A-Za-z]+)\.([^{}]+?)\s*\}\}/g, function (all, ns, name) {
    if (ns === 'random') {
      if (name === 'int') { return String(randomInt()); }
      if (name === 'guid' || name === 'uuid') { return uuidv4(); }
      if (name === 'email') { return randomEmail(); }
      return all;
    }
    if (ns === 'csv') {
      const v = row[name];
      return v === undefined || v === null ? '' : String(v);
    }
    return all;
  });
}

function paramValue(p, row) {
  if (p.mode === 'csv') {
    const v = row[p.column];
    return v === undefined || v === null ? '' : String(v);
  }
  return fill(p.value, row);
}

function buildRequest(ep, row) {
  let path = ep.path;
  const query = [];
  const headers = {};
  for (const p of ep.params) {
    const value = paramValue(p, row);
    if (p.in === 'path') {
      path = path.split('{' + p.name + '}').join(encodeURIComponent(value));
    } else if (p.in === 'query') {
      if (value !== '') { query.push(encodeURIComponent(p.name) + '=' + encodeURIComponent(value)); }
    } else if (p.in === 'header') {
      headers[p.name] = value;
    }
  }
  let body = null;
  if (ep.body !== null) {
    body = fill(ep.body, row);
    headers['Content-Type'] = ep.contentType;
  }
  const separator = path.indexOf('?') >= 0 ? '&' : '?';
  const url = BASE_URL + path + (query.length > 0 ? separator + query.join('&') : '');
  return { url: url, body: body, headers: headers };
}


"""));
    }

    private static void WriteDefaultFunction(StringBuilder sb)
    {
        sb.Append(Lf("""
// ---- Test ------------------------------------------------------------------------
export default function (data) {
  const row = CSV_DATA.length > 0 ? CSV_DATA[Math.floor(Math.random() * CSV_DATA.length)] : {};
  const ep = pickEndpoint();
  const req = buildRequest(ep, row);
  applyAuth(req.headers, data);

  const res = http.request(ep.method, req.url, req.body, {
    headers: req.headers,
    tags: { endpoint: ep.key, name: ep.name, kind: 'test' },
    responseCallback: ep.responseCallback,
  });

  check(res, {
    [ep.check]: function (r) { return ep.expected.indexOf(r.status) >= 0; },
  }, { endpoint: ep.key });

  sleep(THINK_TIME_MIN + Math.random() * (THINK_TIME_MAX - THINK_TIME_MIN));
}

"""));
    }

    /// <summary>Emits a single-quoted JavaScript string literal.</summary>
    private static string Js(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case (char)0x2028: sb.Append(@"\u2028"); break;
                case (char)0x2029: sb.Append(@"\u2029"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>Raw string literals take the source file's line endings; normalize so output is identical on every OS.</summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
