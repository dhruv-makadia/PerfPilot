using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PerfPilot.Infrastructure.Http;

public static class HttpClientNames
{
    public const string Default = "perfpilot-outbound";
    public const string IgnoreTls = "perfpilot-outbound-insecure";

    public static string For(bool ignoreTlsErrors) => ignoreTlsErrors ? IgnoreTls : Default;
}

public sealed record ClassifiedError(string Kind, string Message);

/// <summary>Turns outbound HTTP failures into short, actionable messages for QA engineers.</summary>
public static class HttpErrorClassifier
{
    public static ClassifiedError Classify(Exception ex, string url, TimeSpan timeout, bool timedOut)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

        if (timedOut || ex is TaskCanceledException or TimeoutException)
        {
            return new("Timeout", $"No response from {host} within {timeout.TotalSeconds:0} s. Check that the server is up and reachable from the PerfPilot server (VPN, firewall, proxy).");
        }

        for (var inner = ex; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }:
                    return new("Dns", $"DNS lookup failed for '{host}'. The host name does not resolve from the PerfPilot server — check the URL, VPN connection or internal DNS.");
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused }:
                    return new("ConnectionRefused", $"Connection to {host} was refused. The host resolved but nothing is listening on that port.");
                case SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable } se:
                    return new("Network", $"Network path to {host} is unavailable ({se.SocketErrorCode}). A firewall or missing VPN route is likely blocking the PerfPilot server.");
                case AuthenticationException:
                    return new("Tls", $"TLS/SSL handshake with {host} failed: {inner.Message} If this is an internal staging certificate, enable 'Ignore TLS errors'.");
            }
        }

        if (ex is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
        {
            return new("Dns", $"DNS lookup failed for '{host}'. Check the URL, VPN connection or internal DNS.");
        }
        if (ex is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError })
        {
            return new("Tls", $"TLS/SSL connection to {host} failed. If this is an internal staging certificate, enable 'Ignore TLS errors'.");
        }
        if (ex is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } hre)
        {
            return new("Network", $"Could not connect to {host}: {hre.Message}");
        }

        return new("Network", $"Request to {host} failed: {ex.Message}");
    }

    public static ClassifiedError? ClassifyStatus(HttpStatusCode status, string host) => (int)status switch
    {
        401 => new("Unauthorized", $"{host} returned 401 Unauthorized. The credentials are missing, invalid or expired — check the environment's auth settings."),
        403 => new("Forbidden", $"{host} returned 403 Forbidden. The credentials were accepted but lack permission for this resource."),
        404 => new("NotFound", $"{host} returned 404 Not Found. Check the path."),
        407 => new("ProxyAuth", "An HTTP proxy between PerfPilot and the target requires authentication (407)."),
        >= 500 => new("ServerError", $"{host} returned {(int)status} {status}. The server is reachable but reported an error."),
        >= 400 => new("HttpError", $"{host} returned {(int)status} {status}."),
        _ => null,
    };
}
