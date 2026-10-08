using System.Text.Json;
using System.Text.RegularExpressions;

namespace PerfPilot.Core.Services;

/// <summary>
/// Evaluates the simple JSONPath subset supported for login token extraction:
/// <c>$.a.b</c>, <c>$.items[0].token</c>, <c>$['access_token']</c>, or a bare <c>data.token</c>.
/// The generated k6 script contains an equivalent JavaScript implementation.
/// </summary>
public static partial class JsonPathEvaluator
{
    [GeneratedRegex(@"\[(\d+)\]|\['([^']+)'\]|\[""([^""]+)""\]|\.?([^.\[\]]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"^\$?((\.[^.\[\]]+)|(\[\d+\])|(\['[^']+'\])|(\[""[^""]+""\])|([^.\[\]$][^.\[\]]*))*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidationRegex();

    public static bool IsValid(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Trim() != "$" && ValidationRegex().IsMatch(path.Trim());

    public static bool TryEvaluate(JsonElement root, string path, out JsonElement result)
    {
        result = default;
        var p = path.Trim();
        if (p.StartsWith('$'))
        {
            p = p[1..];
        }

        var current = root;
        foreach (Match m in TokenRegex().Matches(p))
        {
            if (m.Groups[1].Success)
            {
                var index = int.Parse(m.Groups[1].Value);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return false;
                }
                current = current[index];
            }
            else
            {
                var key = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out var next))
                {
                    return false;
                }
                current = next;
            }
        }

        result = current;
        return true;
    }

    /// <summary>Returns the value at <paramref name="path"/> as a string, or null when missing/not scalar.</summary>
    public static string? EvaluateString(string json, string path)
    {
        using var doc = JsonDocument.Parse(json);
        if (!TryEvaluate(doc.RootElement, path, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };
    }
}
