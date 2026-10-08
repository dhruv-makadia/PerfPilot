using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Common;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Options;

namespace PerfPilot.Core.Services;

/// <summary>Validates scenario definitions, including the configured safety caps.</summary>
public sealed partial class ScenarioValidator(IOptions<SafetyOptions> safety)
{
    public static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
    public const int MaxCsvRows = 100_000;
    public const double MaxThinkTimeSeconds = 300;

    [GeneratedRegex(@"\{([^{}/]+)\}", RegexOptions.CultureInvariant)]
    public static partial Regex PathParamRegex();

    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\.([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    public static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderNameRegex();

    public void Validate(string? name, ScenarioDefinition? definition, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "Scenario name is required.");
        }
        else if (name.Trim().Length > 150)
        {
            errors.Add("name", "Scenario name must be 150 characters or fewer.");
        }

        if (definition is null)
        {
            errors.Add("definition", "Scenario definition is required.");
            return;
        }

        var csv = CsvParser.Parse(definition.CsvData);
        ValidateCsv(definition, csv, errors);
        ValidateEndpoints(definition, csv, errors);
        ValidateLoadProfile(definition.LoadProfile, errors);
        ValidateThresholds(definition.GlobalThresholds, "globalThresholds", errors);
    }

    private static void ValidateCsv(ScenarioDefinition definition, CsvTable csv, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(definition.CsvData))
        {
            return;
        }
        if (csv.Columns.Count == 0 || csv.Columns.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("csvData", "CSV data needs a header row with a name for every column.");
        }
        var duplicates = csv.Columns.GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            errors.Add("csvData", $"Duplicate CSV column(s): {string.Join(", ", duplicates)}.");
        }
        if (csv.Rows.Count == 0)
        {
            errors.Add("csvData", "CSV data has a header row but no data rows.");
        }
        if (csv.Rows.Count > MaxCsvRows)
        {
            errors.Add("csvData", $"CSV data is limited to {MaxCsvRows:N0} rows.");
        }
    }

    private static void ValidateEndpoints(ScenarioDefinition definition, CsvTable csv, ValidationErrors errors)
    {
        if (definition.Endpoints.Count == 0)
        {
            errors.Add("endpoints", "Select at least one endpoint.");
            return;
        }

        var columns = csv.Columns.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < definition.Endpoints.Count; i++)
        {
            var ep = definition.Endpoints[i];
            var prefix = $"endpoints[{i}]";
            var label = ep.DisplayName;

            if (!AllowedMethods.Contains(ep.Method?.ToUpperInvariant()))
            {
                errors.Add($"{prefix}.method", $"{label}: unsupported HTTP method '{ep.Method}'.");
            }
            if (string.IsNullOrWhiteSpace(ep.Path) || !ep.Path.StartsWith('/'))
            {
                errors.Add($"{prefix}.path", $"{label}: path must start with '/'.");
            }
            if (ep.Weight <= 0 || double.IsNaN(ep.Weight))
            {
                errors.Add($"{prefix}.weight", $"{label}: weight must be greater than 0.");
            }
            if (ep.ExpectedStatusCodes.Count == 0 || ep.ExpectedStatusCodes.Any(c => c is < 100 or > 599))
            {
                errors.Add($"{prefix}.expectedStatusCodes", $"{label}: specify one or more expected status codes between 100 and 599.");
            }

            foreach (var p in ep.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    errors.Add($"{prefix}.parameters", $"{label}: every parameter needs a name.");
                    continue;
                }
                if (p.In == ParameterLocation.Header && !HeaderNameRegex().IsMatch(p.Name))
                {
                    errors.Add($"{prefix}.parameters", $"{label}: '{p.Name}' is not a valid header name.");
                }
                if (p.Mode == ParameterValueMode.CsvColumn)
                {
                    if (string.IsNullOrWhiteSpace(p.CsvColumn))
                    {
                        errors.Add($"{prefix}.parameters", $"{label}: parameter '{p.Name}' uses CSV mode but no column is selected.");
                    }
                    else if (!columns.Contains(p.CsvColumn))
                    {
                        errors.Add($"{prefix}.parameters", $"{label}: parameter '{p.Name}' references CSV column '{p.CsvColumn}', which is not in the test data.");
                    }
                }
                else
                {
                    ValidatePlaceholders(p.Value, columns, $"{prefix}.parameters", $"{label}: parameter '{p.Name}'", errors);
                }
            }

            if (!string.IsNullOrWhiteSpace(ep.Path))
            {
                foreach (Match m in PathParamRegex().Matches(ep.Path))
                {
                    var paramName = m.Groups[1].Value;
                    var value = ep.Parameters.FirstOrDefault(p => p.In == ParameterLocation.Path && p.Name == paramName);
                    var hasValue = value is not null &&
                        (value.Mode == ParameterValueMode.CsvColumn ? !string.IsNullOrWhiteSpace(value.CsvColumn) : !string.IsNullOrEmpty(value.Value));
                    if (!hasValue)
                    {
                        errors.Add($"{prefix}.parameters", $"{label}: path parameter '{{{paramName}}}' needs a value.");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(ep.Body))
            {
                ValidatePlaceholders(ep.Body, columns, $"{prefix}.body", $"{label}: request body", errors);
                var contentType = ep.ContentType ?? "application/json";
                if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) && !IsJsonWithPlaceholders(ep.Body))
                {
                    errors.Add($"{prefix}.body", $"{label}: request body is not valid JSON.");
                }
            }

            ValidateThresholds(ep.Thresholds, $"{prefix}.thresholds", errors, label);
        }

        var totalWeight = definition.Endpoints.Sum(e => e.Weight);
        if (Math.Abs(totalWeight - 100) > 0.5)
        {
            errors.Add("endpoints", $"Endpoint weights must add up to 100% (currently {totalWeight.ToString("0.##", CultureInfo.InvariantCulture)}%).");
        }
    }

    private static void ValidatePlaceholders(string? text, HashSet<string> columns, string field, string label, ValidationErrors errors)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        foreach (Match m in PlaceholderRegex().Matches(text))
        {
            var ns = m.Groups[1].Value;
            var key = m.Groups[2].Value.Trim();
            if (ns == "random")
            {
                if (key is not ("int" or "guid" or "uuid" or "email"))
                {
                    errors.Add(field, $"{label}: unknown placeholder '{m.Value}'. Use {{{{random.int}}}}, {{{{random.guid}}}} or {{{{random.email}}}}.");
                }
            }
            else if (ns == "csv")
            {
                if (!columns.Contains(key))
                {
                    errors.Add(field, $"{label}: placeholder '{m.Value}' references CSV column '{key}', which is not in the test data.");
                }
            }
            else
            {
                errors.Add(field, $"{label}: unknown placeholder '{m.Value}'.");
            }
        }
    }

    /// <summary>Checks JSON validity, allowing unquoted placeholders such as "id": {{random.int}}.</summary>
    public static bool IsJsonWithPlaceholders(string body)
    {
        var substituted = PlaceholderRegex().Replace(body, "0");
        try
        {
            using var _ = JsonDocument.Parse(substituted);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ValidateLoadProfile(LoadProfile? profile, ValidationErrors errors)
    {
        var caps = safety.Value;
        if (profile is null || profile.Stages.Count == 0)
        {
            errors.Add("loadProfile.stages", "Add at least one load stage.");
            return;
        }

        for (var i = 0; i < profile.Stages.Count; i++)
        {
            var stage = profile.Stages[i];
            if (!DurationParser.TryParse(stage.Duration, out var d) || d <= TimeSpan.Zero)
            {
                errors.Add($"loadProfile.stages[{i}].duration", $"Stage {i + 1}: '{stage.Duration}' is not a valid duration (examples: 30s, 5m, 1h, 1m30s).");
            }
            if (stage.Target < 0)
            {
                errors.Add($"loadProfile.stages[{i}].target", $"Stage {i + 1}: target VUs cannot be negative.");
            }
            if (stage.Target > caps.MaxVus)
            {
                errors.Add($"loadProfile.stages[{i}].target", $"Stage {i + 1}: target of {stage.Target} VUs exceeds the configured cap of {caps.MaxVus} VUs.");
            }
        }

        if (profile.Stages.All(s => s.Target <= 0))
        {
            errors.Add("loadProfile.stages", "At least one stage must have a target above 0 VUs.");
        }

        var total = LoadProfilePresets.TotalDuration(profile.Stages);
        if (total > TimeSpan.FromMinutes(caps.MaxDurationMinutes))
        {
            errors.Add("loadProfile.stages", $"Total duration {DurationParser.Format(total)} exceeds the configured cap of {caps.MaxDurationMinutes} minutes.");
        }

        if (profile.ThinkTimeMinSeconds < 0 || profile.ThinkTimeMaxSeconds < 0)
        {
            errors.Add("loadProfile.thinkTime", "Think time cannot be negative.");
        }
        else if (profile.ThinkTimeMinSeconds > profile.ThinkTimeMaxSeconds)
        {
            errors.Add("loadProfile.thinkTime", "Minimum think time cannot be greater than the maximum.");
        }
        else if (profile.ThinkTimeMaxSeconds > MaxThinkTimeSeconds)
        {
            errors.Add("loadProfile.thinkTime", $"Think time is limited to {MaxThinkTimeSeconds} seconds.");
        }
    }

    private static void ValidateThresholds(SlaThresholds? t, string field, ValidationErrors errors, string? label = null)
    {
        if (t is null)
        {
            return;
        }
        var who = label is null ? "Global SLA" : $"{label} SLA";
        if (t.P95Ms is <= 0)
        {
            errors.Add(field, $"{who}: p95 must be greater than 0 ms.");
        }
        if (t.P99Ms is <= 0)
        {
            errors.Add(field, $"{who}: p99 must be greater than 0 ms.");
        }
        if (t.MaxErrorRatePercent is < 0 or > 100)
        {
            errors.Add(field, $"{who}: max error rate must be between 0 and 100%.");
        }
        if (t.MinRps is < 0)
        {
            errors.Add(field, $"{who}: minimum RPS cannot be negative.");
        }
    }
}
