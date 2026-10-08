using PerfPilot.Core.Domain;
using PerfPilot.Core.Results;

namespace PerfPilot.Tests;

public class HtmlReportGeneratorTests
{
    private static TestRun CompletedRun() => new()
    {
        Id = Guid.NewGuid(),
        ScenarioName = "Checkout <peak>",
        EnvironmentName = "Prod",
        EnvironmentBaseUrl = "https://shop.example.com",
        EnvironmentIsProduction = true,
        StartedBy = "qa.user",
        Status = RunStatus.Completed,
        ThresholdsPassed = false,
        StartedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
        EndedAt = new DateTimeOffset(2026, 1, 1, 10, 5, 0, TimeSpan.Zero),
        ScenarioSnapshot = K6ScriptGeneratorTests.SampleDefinition(),
        Overall = new RunMetrics { Count = 1200, P95Ms = 510, ErrorRate = 0.01, Rps = 20 },
        EndpointResults =
        [
            new RunEndpointResult { Key = "ep1", Name = "GET /pet/{petId}", Method = "GET", Path = "/pet/{petId}", Metrics = new RunMetrics { Count = 840, P95Ms = 310 } },
        ],
        ThresholdResults =
        [
            new RunThresholdResult { EndpointKey = "ep1", ScopeName = "GET /pet/{petId}", Metric = SlaMetric.P95, Limit = 300, Actual = 310, Passed = false },
        ],
        TimeSeries =
        [
            new TimeSeriesPoint { T = 0, Vus = 5, Requests = 10, Rps = 5, P50Ms = 100, P95Ms = 200, P99Ms = 300 },
            new TimeSeriesPoint { T = 2, Vus = 10, Requests = 0, Rps = 0 },
            new TimeSeriesPoint { T = 4, Vus = 10, Requests = 20, Rps = 10, P50Ms = 110, P95Ms = 210, P99Ms = 310 },
        ],
    };

    [Fact]
    public void Renders_self_contained_report_with_verdict_breaches_and_charts()
    {
        var html = new HtmlReportGenerator(TimeProvider.System).Generate(CompletedRun());

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("FAILED", html);
        Assert.Contains("1 of 1 SLA threshold(s) were breached.", html);
        Assert.Contains("Checkout &lt;peak&gt;", html);
        Assert.DoesNotContain("<peak>", html);
        Assert.Contains("PRODUCTION", html);
        Assert.Contains("breach-cell", html);
        Assert.Equal(3, html.Split("<svg").Length - 1);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", ""));
    }
}
