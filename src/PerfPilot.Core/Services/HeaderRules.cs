using System.Text.RegularExpressions;

namespace PerfPilot.Core.Services;

public static partial class HeaderRules
{
    public const string ContentType = "Content-Type";
    public const string DefaultLoginContentType = "application/json";

    /// <summary>Headers the HTTP stack manages itself; configuring them would break or be ignored.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection",
    };

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) => !string.IsNullOrEmpty(name) && NameRegex().IsMatch(name);

    public static bool IsReserved(string name) => Reserved.Contains(name);

    public static bool IsValidValue(string value) => !value.Contains('\r') && !value.Contains('\n');

    public static bool IsJson(string contentType) => contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Headers for the login request: the configured ones, plus <c>Content-Type: application/json</c>
    /// unless a Content-Type was configured. Order is preserved.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildLoginHeaders(IReadOnlyDictionary<string, string>? configured)
    {
        var result = new Dictionary<string, string>();
        if (configured?.Keys.Any(k => k.Equals(ContentType, StringComparison.OrdinalIgnoreCase)) != true)
        {
            result[ContentType] = DefaultLoginContentType;
        }
        foreach (var (name, value) in configured ?? new Dictionary<string, string>())
        {
            result[name.Equals(ContentType, StringComparison.OrdinalIgnoreCase) ? ContentType : name] = value;
        }
        return result;
    }
}
