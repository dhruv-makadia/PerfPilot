using System.Globalization;
using System.Text.RegularExpressions;

namespace PerfPilot.Core.Services;

/// <summary>Parses k6 duration strings such as "30s", "5m", "1h30m", "1m30s" or "500ms".</summary>
public static partial class DurationParser
{
    [GeneratedRegex(@"^(?:(?<v>\d+(?:\.\d+)?)(?<u>ms|s|m|h))+$", RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();

    public static bool TryParse(string? value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = DurationRegex().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        double totalMs = 0;
        var values = match.Groups["v"].Captures;
        var units = match.Groups["u"].Captures;
        for (var i = 0; i < values.Count; i++)
        {
            var number = double.Parse(values[i].Value, CultureInfo.InvariantCulture);
            totalMs += units[i].Value switch
            {
                "ms" => number,
                "s" => number * 1000,
                "m" => number * 60_000,
                "h" => number * 3_600_000,
                _ => 0,
            };
        }

        duration = TimeSpan.FromMilliseconds(totalMs);
        return true;
    }

    public static TimeSpan Parse(string value) =>
        TryParse(value, out var d) ? d : throw new FormatException($"'{value}' is not a valid duration (examples: 30s, 5m, 1h, 1m30s).");

    /// <summary>Formats a duration compactly, e.g. 90s → "1m30s".</summary>
    public static string Format(TimeSpan duration)
    {
        var totalSeconds = (long)Math.Round(duration.TotalSeconds);
        if (totalSeconds <= 0)
        {
            return "0s";
        }
        var h = totalSeconds / 3600;
        var m = totalSeconds % 3600 / 60;
        var s = totalSeconds % 60;
        return (h > 0 ? $"{h}h" : "") + (m > 0 ? $"{m}m" : "") + (s > 0 ? $"{s}s" : "");
    }
}
