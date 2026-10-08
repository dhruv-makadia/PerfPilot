using System.Text.Json;
using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;

namespace PerfPilot.Core.Results;

public sealed record EndpointSummary(RunMetrics Metrics, long ChecksPassed, long ChecksFailed);

public sealed record ParsedSummary(
    RunMetrics Overall,
    IReadOnlyDictionary<string, EndpointSummary> Endpoints,
    IReadOnlyList<RunThresholdResult> Thresholds,
    long ChecksPassed,
    long ChecksFailed,
    bool AnyK6ThresholdFailed);

/// <summary>
/// Parses the JSON written by <c>k6 run --summary-export</c>.
/// In that format a threshold value of <c>true</c> means the threshold FAILED.
/// </summary>
public static class K6SummaryParser
{
    public static ParsedSummary Parse(string json, IReadOnlyList<ThresholdDefinition> plan, IReadOnlyList<K6EndpointInfo> endpoints)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("k6 summary JSON has no 'metrics' object.");
        }

        var overall = ReadMetrics(metrics, null);
        var checksByName = ReadChecks(root);

        var endpointSummaries = new Dictionary<string, EndpointSummary>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            checksByName.TryGetValue(ep.CheckName, out var checks);
            endpointSummaries[ep.Key] = new EndpointSummary(ReadMetrics(metrics, ep.Key), checks.Passes, checks.Fails);
        }

        var thresholds = new List<RunThresholdResult>();
        for (var i = 0; i < plan.Count; i++)
        {
            thresholds.Add(EvaluateThreshold(metrics, plan[i], i));
        }

        var checksMetric = Get(metrics, "checks");
        long checksPassed = checksMetric is { } c ? (long)Number(c, "passes") : checksByName.Values.Sum(v => v.Passes);
        long checksFailed = checksMetric is { } f ? (long)Number(f, "fails") : checksByName.Values.Sum(v => v.Fails);

        return new ParsedSummary(overall, endpointSummaries, thresholds, checksPassed, checksFailed, AnyThresholdFailed(metrics));
    }

    /// <summary>Reads overall (endpointKey null) or per-endpoint metrics from the tagged submetrics.</summary>
    public static RunMetrics ReadMetrics(JsonElement metrics, string? endpointKey)
    {
        // Overall metrics come from the {kind:test} submetrics so login requests are excluded;
        // fall back to the untagged metrics for scripts that don't use PerfPilot's tags.
        var duration = Get(metrics, K6ScriptGenerator.DurationMetric(endpointKey)) ?? (endpointKey is null ? Get(metrics, "http_req_duration") : null);
        var failed = Get(metrics, K6ScriptGenerator.FailedMetric(endpointKey)) ?? (endpointKey is null ? Get(metrics, "http_req_failed") : null);
        var requests = Get(metrics, K6ScriptGenerator.RequestsMetric(endpointKey)) ?? (endpointKey is null ? Get(metrics, "http_reqs") : null);

        var result = new RunMetrics();
        if (duration is { } d)
        {
            result.AvgMs = Number(d, "avg");
            result.MinMs = Number(d, "min");
            result.P50Ms = Number(d, "med");
            result.P90Ms = Number(d, "p(90)");
            result.P95Ms = Number(d, "p(95)");
            result.P99Ms = Number(d, "p(99)");
            result.MaxMs = Number(d, "max");
            result.Count = (long)Number(d, "count");
        }
        if (requests is { } r)
        {
            result.Count = (long)Number(r, "count");
            result.Rps = Number(r, "rate");
        }
        if (failed is { } f)
        {
            // http_req_failed is a Rate: "passes" counts true values, i.e. failed requests.
            result.ErrorRate = Number(f, "value");
            result.Failed = (long)Number(f, "passes");
        }
        return result;
    }

    private static RunThresholdResult EvaluateThreshold(JsonElement metrics, ThresholdDefinition def, int order)
    {
        var result = new RunThresholdResult
        {
            EndpointKey = def.EndpointKey,
            ScopeName = def.ScopeName,
            Metric = def.Metric,
            Limit = def.Limit,
            K6Metric = def.K6Metric,
            Expression = def.Expression,
            SortOrder = order,
        };

        var metric = Get(metrics, def.K6Metric);
        if (metric is not { } m)
        {
            result.Actual = null;
            result.Passed = false;
            return result;
        }

        double? actual = def.Metric switch
        {
            SlaMetric.P95 => OptionalNumber(m, "p(95)"),
            SlaMetric.P99 => OptionalNumber(m, "p(99)"),
            SlaMetric.ErrorRate => OptionalNumber(m, "value") is { } v ? v * 100 : null,
            SlaMetric.MinRps => OptionalNumber(m, "rate"),
            _ => null,
        };
        result.Actual = actual;

        if (m.TryGetProperty("thresholds", out var flags) && flags.ValueKind == JsonValueKind.Object &&
            flags.TryGetProperty(def.Expression, out var failedFlag) && failedFlag.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result.Passed = failedFlag.ValueKind == JsonValueKind.False;
        }
        else if (actual is { } a)
        {
            result.Passed = def.Metric == SlaMetric.MinRps ? a >= def.Limit : a <= def.Limit;
        }
        else
        {
            result.Passed = false;
        }
        return result;
    }

    private static bool AnyThresholdFailed(JsonElement metrics)
    {
        foreach (var metric in metrics.EnumerateObject())
        {
            if (metric.Value.ValueKind == JsonValueKind.Object &&
                metric.Value.TryGetProperty("thresholds", out var flags) && flags.ValueKind == JsonValueKind.Object &&
                flags.EnumerateObject().Any(t => t.Value.ValueKind == JsonValueKind.True))
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, (long Passes, long Fails)> ReadChecks(JsonElement root)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        if (root.TryGetProperty("root_group", out var group))
        {
            CollectChecks(group, result);
        }
        return result;
    }

    private static void CollectChecks(JsonElement group, Dictionary<string, (long Passes, long Fails)> result)
    {
        if (group.TryGetProperty("checks", out var checks))
        {
            // Newer k6 versions emit an object keyed by check name; older ones an array.
            var items = checks.ValueKind switch
            {
                JsonValueKind.Object => checks.EnumerateObject().Select(p => p.Value).ToList(),
                JsonValueKind.Array => checks.EnumerateArray().ToList(),
                _ => [],
            };
            foreach (var check in items)
            {
                if (check.TryGetProperty("name", out var nameEl) && nameEl.GetString() is { } name)
                {
                    var existing = result.GetValueOrDefault(name);
                    result[name] = (existing.Passes + (long)Number(check, "passes"), existing.Fails + (long)Number(check, "fails"));
                }
            }
        }

        if (group.TryGetProperty("groups", out var groups))
        {
            var children = groups.ValueKind switch
            {
                JsonValueKind.Object => groups.EnumerateObject().Select(p => p.Value).ToList(),
                JsonValueKind.Array => groups.EnumerateArray().ToList(),
                _ => [],
            };
            foreach (var child in children)
            {
                CollectChecks(child, result);
            }
        }
    }

    private static JsonElement? Get(JsonElement metrics, string name) =>
        metrics.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static double Number(JsonElement element, string property) => OptionalNumber(element, property) ?? 0;

    private static double? OptionalNumber(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
