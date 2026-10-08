using System.Text.Json;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Common;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Options;
using PerfPilot.Core.Services;
using PerfPilot.Infrastructure.K6;

namespace PerfPilot.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("1m30s", 90)]
    [InlineData("1h5m10s", 3910)]
    [InlineData("500ms", 0.5)]
    [InlineData("1.5m", 90)]
    public void Parses_k6_durations(string value, double seconds)
    {
        Assert.True(DurationParser.TryParse(value, out var d));
        Assert.Equal(seconds, d.TotalSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("5 minutes")]
    [InlineData("-5s")]
    public void Rejects_invalid_durations(string value) => Assert.False(DurationParser.TryParse(value, out _));

    [Fact]
    public void Formats_compactly() => Assert.Equal("1h1m30s", DurationParser.Format(TimeSpan.FromSeconds(3690)));
}

public class CsvParserTests
{
    [Fact]
    public void Parses_quoted_fields_and_crlf()
    {
        var table = CsvParser.Parse("id,name\r\n1,\"Smith, John\"\r\n2,\"say \"\"hi\"\"\"\r\n");

        Assert.Equal(["id", "name"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Smith, John", table.Rows[0]["name"]);
        Assert.Equal("say \"hi\"", table.Rows[1]["name"]);
    }

    [Fact]
    public void Detects_tab_separated_data_pasted_from_spreadsheets()
    {
        var table = CsvParser.Parse("user\temail\nann\tann@x.io\n");
        Assert.Equal("ann@x.io", table.Rows[0]["email"]);
    }

    [Fact]
    public void Missing_trailing_values_become_empty_strings()
    {
        var table = CsvParser.Parse("a,b\n1\n");
        Assert.Equal("", table.Rows[0]["b"]);
    }
}

public class JsonPathEvaluatorTests
{
    private const string Json = """{"access_token":"abc","data":{"items":[{"token":"t0"},{"token":42}]},"odd key":{"v":"x"}}""";

    [Theory]
    [InlineData("$.access_token", "abc")]
    [InlineData("access_token", "abc")]
    [InlineData("$.data.items[0].token", "t0")]
    [InlineData("$.data.items[1].token", "42")]
    [InlineData("$['odd key'].v", "x")]
    public void Extracts_values(string path, string expected) => Assert.Equal(expected, JsonPathEvaluator.EvaluateString(Json, path));

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.data.items[5].token")]
    [InlineData("$.data")]
    public void Returns_null_for_missing_or_non_scalar(string path) => Assert.Null(JsonPathEvaluator.EvaluateString(Json, path));

    [Theory]
    [InlineData("$.a.b", true)]
    [InlineData("$['a'][0]", true)]
    [InlineData("$", false)]
    [InlineData("", false)]
    [InlineData("$..a", false)]
    public void Validates_supported_syntax(string path, bool valid) => Assert.Equal(valid, JsonPathEvaluator.IsValid(path));
}

public class ScenarioValidatorTests
{
    private static readonly ScenarioValidator Validator = new(Options.Create(new SafetyOptions { MaxVus = 50, MaxDurationMinutes = 10 }));

    private static Dictionary<string, string[]> Errors(ScenarioDefinition def)
    {
        var errors = new ValidationErrors();
        Validator.Validate("Scenario", def, errors);
        try
        {
            errors.ThrowIfAny();
            return [];
        }
        catch (ValidationException ex)
        {
            return ex.Errors.ToDictionary(k => k.Key, v => v.Value);
        }
    }

    [Fact]
    public void Sample_definition_is_valid_within_caps() =>
        Assert.Empty(Errors(K6ScriptGeneratorTests.SampleDefinition()));

    [Fact]
    public void Enforces_vu_and_duration_caps()
    {
        var def = K6ScriptGeneratorTests.SampleDefinition();
        def.LoadProfile.Stages = [new LoadStage { Duration = "11m", Target = 51 }];

        var all = string.Join(" ", Errors(def).SelectMany(e => e.Value));
        Assert.Contains("exceeds the configured cap of 50 VUs", all);
        Assert.Contains("exceeds the configured cap of 10 minutes", all);
    }

    [Fact]
    public void Requires_weights_to_total_100_and_path_parameters_to_have_values()
    {
        var def = K6ScriptGeneratorTests.SampleDefinition();
        def.Endpoints[0].Weight = 50;
        def.Endpoints[0].Parameters.RemoveAt(0);

        var all = string.Join(" ", Errors(def).SelectMany(e => e.Value));
        Assert.Contains("must add up to 100%", all);
        Assert.Contains("path parameter '{petId}' needs a value", all);
    }

    [Fact]
    public void Rejects_unknown_placeholders_csv_columns_and_invalid_json()
    {
        var def = K6ScriptGeneratorTests.SampleDefinition();
        def.Endpoints[1].Body = "{\"a\": \"{{csv.nope}}\", \"b\": {{random.date}}";

        var all = string.Join(" ", Errors(def).SelectMany(e => e.Value));
        Assert.Contains("CSV column 'nope'", all);
        Assert.Contains("unknown placeholder '{{random.date}}'", all);
        Assert.Contains("not valid JSON", all);
    }

    [Fact]
    public void Allows_unquoted_placeholders_in_json() =>
        Assert.True(ScenarioValidator.IsJsonWithPlaceholders("{\"id\": {{random.int}}, \"n\": \"{{random.email}}\"}"));
}

public class MetricsAggregatorTests
{
    private static string Point(string metric, double seconds, double value, string? kind = "test") =>
        JsonSerializer.Serialize(new
        {
            metric,
            type = "Point",
            data = new
            {
                time = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(5.5)).AddSeconds(seconds).ToString("O"),
                value,
                tags = kind is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["kind"] = kind },
            },
        });

    [Fact]
    public void Builds_live_snapshots_and_series_from_json_lines_excluding_auth_requests()
    {
        var agg = new MetricsAggregator(bucketSeconds: 2);
        agg.IngestLine("""{"type":"Metric","data":{"name":"vus","type":"gauge"},"metric":"vus"}""");
        agg.IngestLine(Point("vus", 0, 3, null));
        agg.IngestLine(Point("http_req_duration", 0.1, 999, "auth"));
        agg.IngestLine(Point("http_req_failed", 0.1, 1, "auth"));
        for (var i = 0; i < 10; i++)
        {
            agg.IngestLine(Point("http_req_duration", 0.2 * i, 100 + i * 10));
            agg.IngestLine(Point("http_req_failed", 0.2 * i, i == 0 ? 1 : 0));
        }
        agg.IngestLine(Point("http_req_duration", 3.0, 500));
        agg.IngestLine(Point("http_req_failed", 3.0, 0));
        agg.IngestLine("not json at all");

        var live = agg.TakeLiveSnapshot()!;
        Assert.Equal(11, live.TotalRequests);
        Assert.Equal(1, live.TotalFailed);
        Assert.Equal(3, live.Vus);
        Assert.Equal(3, live.ElapsedSeconds);
        Assert.Equal(11 / 3.0, live.Rps, 3);

        var series = agg.BuildSeries();
        Assert.Equal(2, series.Count);
        Assert.Equal(0, series[0].T);
        Assert.Equal(10, series[0].Requests);
        Assert.Equal(5, series[0].Rps);
        Assert.Equal(0.1, series[0].ErrorRate);
        Assert.Equal(3, series[0].Vus);
        Assert.Equal(145, series[0].P50Ms);
        Assert.Equal(2, series[1].T);
        Assert.Equal(1, series[1].Requests);

        // Next snapshot only covers new samples.
        agg.IngestLine(Point("http_req_duration", 4.0, 50));
        var next = agg.TakeLiveSnapshot()!;
        Assert.Equal(1, next.Point.Requests);
        Assert.Equal(50, next.P95Ms);
    }

    [Fact]
    public void Percentile_matches_linear_interpolation()
    {
        double[] sorted = [1, 2, 3, 4];
        Assert.Equal(2.5, MetricsAggregator.Percentile(sorted, 50));
        Assert.Equal(3.85, MetricsAggregator.Percentile(sorted, 95), 5);
        Assert.Equal(0, MetricsAggregator.Percentile([], 95));
    }
}

public class K6OutputTailerTests
{
    [Fact]
    public async Task Reads_lines_appended_while_writer_runs_and_drains_after_exit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pp-tail-{Guid.NewGuid():N}.json");
        var lines = new List<string>();
        var done = false;
        try
        {
            var tail = K6OutputTailer.TailAsync(path, lines.Add, () => Volatile.Read(ref done), CancellationToken.None, pollMs: 20);
            await using (var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)))
            {
                await writer.WriteAsync("{\"a\":1}\n{\"b\":");
                await writer.FlushAsync();
                await Task.Delay(100);
                await writer.WriteAsync("2}\r\n{\"c\":3}");
                await writer.FlushAsync();
            }
            Volatile.Write(ref done, true);
            await tail.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(["{\"a\":1}", "{\"b\":2}", "{\"c\":3}"], lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Describes_k6_failures_using_error_lines()
    {
        var message = RunExecutor.DescribeFailure(107, ["running", "time=\"x\" level=error msg=\"ReferenceError: foo is not defined\"", "done"]);
        Assert.StartsWith("the script threw an exception: ", message);
        Assert.Contains("ReferenceError", message);
    }
}
