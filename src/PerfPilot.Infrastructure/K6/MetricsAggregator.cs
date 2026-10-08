using System.Globalization;
using System.Text.Json;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.K6;

public sealed record LiveSnapshot(double ElapsedSeconds, int Vus, double Rps, double P95Ms, double ErrorRate, long TotalRequests, long TotalFailed, TimeSeriesPoint Point);

/// <summary>
/// Aggregates the k6 <c>--out json</c> stream into live snapshots and a persisted time series.
/// Times come from the samples themselves, so k6's output buffering doesn't distort rates.
/// Login requests (tag kind=auth) are excluded, matching the SLA thresholds.
/// </summary>
public sealed class MetricsAggregator(double bucketSeconds, int maxSamplesPerWindow = 10_000)
{
    private readonly object _lock = new();
    private readonly Random _random = new(42);
    private readonly SortedDictionary<long, Window> _buckets = new();
    private Window _live = new();
    private DateTimeOffset? _start;
    private DateTimeOffset _maxTime;
    private DateTimeOffset? _lastSnapshotTime;
    private int _currentVus;
    private long _totalRequests;
    private long _totalFailed;

    public double BucketSeconds { get; } = Math.Max(1, bucketSeconds);

    public long TotalRequests
    {
        get { lock (_lock) { return _totalRequests; } }
    }

    private sealed class Window
    {
        public readonly List<double> Durations = [];
        public long Requests;
        public long Failed;
        public int MaxVus;
    }

    /// <summary>Ingests one line of k6 JSON output. Non-point lines and unrelated metrics are ignored.</summary>
    public void IngestLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"Point\"", StringComparison.Ordinal))
        {
            return;
        }

        string? metric;
        DateTimeOffset time;
        double value;
        string? kind = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "Point")
            {
                return;
            }
            metric = root.GetProperty("metric").GetString();
            if (metric is not ("http_req_duration" or "http_req_failed" or "vus"))
            {
                return;
            }
            var data = root.GetProperty("data");
            time = DateTimeOffset.Parse(data.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
            value = data.GetProperty("value").GetDouble();
            if (data.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("kind", out var k))
            {
                kind = k.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return;
        }

        AddSample(metric!, time, value, kind);
    }

    public void AddSample(string metric, DateTimeOffset time, double value, string? kind)
    {
        lock (_lock)
        {
            _start ??= time;
            if (time > _maxTime)
            {
                _maxTime = time;
            }
            var bucket = GetBucket(time);

            switch (metric)
            {
                case "vus":
                    _currentVus = (int)value;
                    bucket.MaxVus = Math.Max(bucket.MaxVus, _currentVus);
                    _live.MaxVus = Math.Max(_live.MaxVus, _currentVus);
                    break;

                case "http_req_duration" when kind != "auth":
                    _totalRequests++;
                    AddDuration(bucket, value);
                    AddDuration(_live, value);
                    break;

                case "http_req_failed" when kind != "auth" && value >= 1:
                    _totalFailed++;
                    bucket.Failed++;
                    _live.Failed++;
                    break;
            }
        }
    }

    /// <summary>Returns metrics for samples received since the previous snapshot, or null before the first sample.</summary>
    public LiveSnapshot? TakeLiveSnapshot()
    {
        lock (_lock)
        {
            if (_start is not { } start)
            {
                return null;
            }

            var windowStart = _lastSnapshotTime ?? start;
            var span = Math.Max((_maxTime - windowStart).TotalSeconds, 1);
            var window = _live;
            _live = new Window();
            _lastSnapshotTime = _maxTime;

            var point = ToPoint(window, (_maxTime - start).TotalSeconds, span, Math.Max(window.MaxVus, _currentVus));
            return new LiveSnapshot(point.T, _currentVus, point.Rps, point.P95Ms, point.ErrorRate, _totalRequests, _totalFailed, point);
        }
    }

    /// <summary>Builds the persisted time series (one point per bucket).</summary>
    public List<TimeSeriesPoint> BuildSeries()
    {
        lock (_lock)
        {
            if (_start is not { } start)
            {
                return [];
            }
            var lastIndex = _buckets.Count > 0 ? _buckets.Keys.Max() : 0;
            var result = new List<TimeSeriesPoint>(_buckets.Count);
            foreach (var (index, bucket) in _buckets)
            {
                var bucketStart = index * BucketSeconds;
                var span = index == lastIndex
                    ? Math.Clamp((_maxTime - start).TotalSeconds - bucketStart, 1, BucketSeconds)
                    : BucketSeconds;
                result.Add(ToPoint(bucket, bucketStart, span, bucket.MaxVus));
            }
            return result;
        }
    }

    private Window GetBucket(DateTimeOffset time)
    {
        var index = (long)Math.Floor(Math.Max(0, (time - _start!.Value).TotalSeconds) / BucketSeconds);
        if (!_buckets.TryGetValue(index, out var bucket))
        {
            bucket = new Window();
            _buckets[index] = bucket;
        }
        return bucket;
    }

    /// <summary>Reservoir sampling keeps percentile estimates accurate with bounded memory on long runs.</summary>
    private void AddDuration(Window window, double value)
    {
        window.Requests++;
        if (window.Durations.Count < maxSamplesPerWindow)
        {
            window.Durations.Add(value);
            return;
        }
        var slot = _random.NextInt64(window.Requests);
        if (slot < maxSamplesPerWindow)
        {
            window.Durations[(int)slot] = value;
        }
    }

    private static TimeSeriesPoint ToPoint(Window window, double t, double spanSeconds, int vus)
    {
        var sorted = window.Durations.ToArray();
        Array.Sort(sorted);
        return new TimeSeriesPoint
        {
            T = Math.Round(t, 3),
            Vus = vus,
            Requests = window.Requests,
            Rps = Math.Round(window.Requests / spanSeconds, 3),
            P50Ms = Math.Round(Percentile(sorted, 50), 3),
            P95Ms = Math.Round(Percentile(sorted, 95), 3),
            P99Ms = Math.Round(Percentile(sorted, 99), 3),
            ErrorRate = window.Requests == 0 ? 0 : Math.Round((double)window.Failed / window.Requests, 5),
        };
    }

    /// <summary>Linear-interpolated percentile (same method k6 uses).</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }
        var rank = p / 100 * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }
}
