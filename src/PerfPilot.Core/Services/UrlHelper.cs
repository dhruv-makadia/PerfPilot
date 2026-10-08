namespace PerfPilot.Core.Services;

public static class UrlHelper
{
    public static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Resolves a login URL that may be absolute or a path relative to the base URL.</summary>
    public static string Resolve(string baseUrl, string urlOrPath)
    {
        if (IsAbsoluteHttpUrl(urlOrPath))
        {
            return urlOrPath.Trim();
        }
        return baseUrl.TrimEnd('/') + "/" + urlOrPath.Trim().TrimStart('/');
    }

    public static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');
}
