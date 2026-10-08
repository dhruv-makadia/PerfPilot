using PerfPilot.Core.Domain;
using PerfPilot.Core.K6;

namespace PerfPilot.Core.Results;

public static class RunResultMapper
{
    /// <summary>Copies parsed k6 summary results onto a run and computes the SLA verdict.</summary>
    public static void Apply(TestRun run, ParsedSummary summary, IReadOnlyList<K6EndpointInfo> endpoints)
    {
        run.Overall = summary.Overall;
        run.ChecksPassed = summary.ChecksPassed;
        run.ChecksFailed = summary.ChecksFailed;

        run.EndpointResults = endpoints.Select((ep, i) =>
        {
            var s = summary.Endpoints.GetValueOrDefault(ep.Key);
            return new RunEndpointResult
            {
                Id = Guid.NewGuid(),
                TestRunId = run.Id,
                Key = ep.Key,
                Name = ep.Name,
                Method = ep.Method,
                Path = ep.Path,
                Metrics = s?.Metrics ?? new RunMetrics(),
                ChecksPassed = s?.ChecksPassed ?? 0,
                ChecksFailed = s?.ChecksFailed ?? 0,
                SortOrder = i,
            };
        }).ToList();

        run.ThresholdResults = summary.Thresholds.Select(t =>
        {
            t.Id = Guid.NewGuid();
            t.TestRunId = run.Id;
            return t;
        }).ToList();

        run.ThresholdsPassed = run.ThresholdResults.All(t => t.Passed);
    }
}
