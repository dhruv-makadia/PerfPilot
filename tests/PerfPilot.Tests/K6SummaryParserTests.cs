using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;
using PerfPilot.Core.Results;

namespace PerfPilot.Tests;

public class K6SummaryParserTests
{
    private static (ParsedSummary Summary, IReadOnlyList<K6EndpointInfo> Endpoints, K6Script Script) ParseFixture()
    {
        var def = K6ScriptGeneratorTests.SampleDefinition();
        var script = new K6ScriptGenerator().Generate(new K6ScriptInput("Fixture", new K6EnvironmentInfo(
            "Staging", "https://x.local", false, AuthType.None, null, null, null, "Authorization", "Bearer ", LoginScope.PerVu), def));
        var endpoints = K6ScriptGenerator.DescribeEndpoints(def);
        return (K6SummaryParser.Parse(TestFiles.ReadFixture("k6-summary.json"), script.Thresholds, endpoints), endpoints, script);
    }

    [Fact]
    public void Reads_overall_metrics_from_test_tagged_submetrics_excluding_login()
    {
        var overall = ParseFixture().Summary.Overall;

        Assert.Equal(1200, overall.Count);
        Assert.Equal(240.25, overall.AvgMs);
        Assert.Equal(95.5, overall.MinMs);
        Assert.Equal(200.75, overall.P50Ms);
        Assert.Equal(380.1, overall.P90Ms);
        Assert.Equal(510.2, overall.P95Ms);
        Assert.Equal(880.9, overall.P99Ms);
        Assert.Equal(1400.5, overall.MaxMs);
        Assert.Equal(0.01, overall.ErrorRate);
        Assert.Equal(12, overall.Failed);
        Assert.Equal(20, overall.Rps);
    }

    [Fact]
    public void Reads_per_endpoint_metrics_and_checks()
    {
        var summary = ParseFixture().Summary;

        var ep1 = summary.Endpoints["ep1"];
        Assert.Equal(840, ep1.Metrics.Count);
        Assert.Equal(310.4, ep1.Metrics.P95Ms);
        Assert.Equal(650.2, ep1.Metrics.P99Ms);
        Assert.Equal(14, ep1.Metrics.Rps);
        Assert.Equal(0, ep1.Metrics.ErrorRate);
        Assert.Equal(840, ep1.ChecksPassed);

        var ep2 = summary.Endpoints["ep2"];
        Assert.Equal(360, ep2.Metrics.Count);
        Assert.Equal(12, ep2.Metrics.Failed);
        Assert.Equal(0.03333333333333333, ep2.Metrics.ErrorRate, 10);
        Assert.Equal(348, ep2.ChecksPassed);
        Assert.Equal(12, ep2.ChecksFailed);

        Assert.Equal(1188, summary.ChecksPassed);
        Assert.Equal(12, summary.ChecksFailed);
    }

    [Fact]
    public void Interprets_threshold_flags_true_as_failed()
    {
        var summary = ParseFixture().Summary;

        Assert.Equal(
        [
            "Global P95 510.2 pass",
            "Global ErrorRate 1 pass",
            "Global MinRps 20 pass",
            "GET /pet/{petId} P95 310.4 FAIL",
            "GET /pet/{petId} P99 650.2 FAIL",
            "Create pet ErrorRate 3.33 FAIL",
            "Create pet MinRps 6 pass",
        ], summary.Thresholds.Select(t => $"{t.ScopeName} {t.Metric} {Math.Round(t.Actual!.Value, 2)} {(t.Passed ? "pass" : "FAIL")}"));
        Assert.True(summary.AnyK6ThresholdFailed);
    }

    [Fact]
    public void Result_mapper_sets_verdict_and_endpoint_rows()
    {
        var (summary, endpoints, _) = ParseFixture();
        var run = new TestRun { Id = Guid.NewGuid() };

        RunResultMapper.Apply(run, summary, endpoints);

        Assert.False(run.ThresholdsPassed);
        Assert.Equal(2, run.EndpointResults.Count);
        Assert.Equal("Create pet", run.EndpointResults[1].Name);
        Assert.Equal("/pet", run.EndpointResults[1].Path);
        Assert.All(run.EndpointResults, r => Assert.Equal(run.Id, r.TestRunId));
        Assert.All(run.ThresholdResults, r => Assert.NotEqual(Guid.Empty, r.Id));
        Assert.Equal(1200, run.Overall.Count);
    }

    [Fact]
    public void Falls_back_to_computed_verdict_when_flag_missing_and_handles_missing_metrics()
    {
        const string json = """
        { "metrics": { "http_req_duration{kind:test}": { "p(95)": 120, "p(99)": 400 } }, "root_group": { "checks": [] } }
        """;
        var plan = new List<ThresholdDefinition>
        {
            new() { ScopeName = "Global", Metric = SlaMetric.P95, Limit = 200, K6Metric = "http_req_duration{kind:test}", Expression = "p(95)<=200" },
            new() { ScopeName = "Global", Metric = SlaMetric.P99, Limit = 300, K6Metric = "http_req_duration{kind:test}", Expression = "p(99)<=300" },
            new() { ScopeName = "Global", Metric = SlaMetric.MinRps, Limit = 1, K6Metric = "http_reqs{kind:test}", Expression = "rate>=1" },
        };

        var summary = K6SummaryParser.Parse(json, plan, []);

        Assert.True(summary.Thresholds[0].Passed);
        Assert.False(summary.Thresholds[1].Passed);
        Assert.False(summary.Thresholds[2].Passed);
        Assert.Null(summary.Thresholds[2].Actual);
        Assert.False(summary.AnyK6ThresholdFailed);
    }

    [Fact]
    public void Falls_back_to_untagged_metrics_for_foreign_scripts()
    {
        const string json = """
        { "metrics": {
            "http_req_duration": { "avg": 10, "p(95)": 20, "count": 5 },
            "http_reqs": { "count": 5, "rate": 2.5 },
            "http_req_failed": { "passes": 1, "fails": 4, "value": 0.2 } } }
        """;

        var overall = K6SummaryParser.Parse(json, [], []).Overall;

        Assert.Equal(5, overall.Count);
        Assert.Equal(20, overall.P95Ms);
        Assert.Equal(2.5, overall.Rps);
        Assert.Equal(0.2, overall.ErrorRate);
        Assert.Equal(1, overall.Failed);
    }

    [Fact]
    public void Throws_on_invalid_summary()
    {
        Assert.Throws<FormatException>(() => K6SummaryParser.Parse("{}", [], []));
    }
}
