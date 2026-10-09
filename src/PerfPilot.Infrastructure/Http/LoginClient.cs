using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Services;

namespace PerfPilot.Infrastructure.Http;

public sealed record LoginAttempt(bool Success, string Url, int? StatusCode, long LatencyMs, string? ErrorKind, string Message, string? Token);

/// <summary>Runs an environment's login flow server-side (connection tests and authenticated spec downloads).</summary>
internal sealed class LoginClient(IHttpClientFactory httpClientFactory) : ILoginClient
{
    public async Task<string> GetTokenAsync(string baseUrl, ResolvedAuth auth, bool ignoreTlsErrors, TimeSpan timeout, CancellationToken ct)
    {
        var attempt = await TryLoginAsync(baseUrl, auth, ignoreTlsErrors, timeout, ct);
        return attempt.Success && attempt.Token is not null
            ? attempt.Token
            : throw new ExternalCallException("Login flow failed: " + attempt.Message, attempt.ErrorKind ?? "Login");
    }

    public async Task<LoginAttempt> TryLoginAsync(string baseUrl, ResolvedAuth auth, bool ignoreTlsErrors, TimeSpan timeout, CancellationToken ct)
    {
        var url = UrlHelper.Resolve(baseUrl, auth.LoginUrl ?? "/");
        var host = new Uri(url).Host;
        var client = httpClientFactory.CreateClient(HttpClientNames.For(ignoreTlsErrors));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = BuildRequest(url, auth);
            using var response = await client.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            if (HttpErrorClassifier.ClassifyStatus(response.StatusCode, host) is { } statusError)
            {
                return new(false, url, (int)response.StatusCode, sw.ElapsedMilliseconds, statusError.Kind, statusError.Message, null);
            }

            string? token;
            try
            {
                token = JsonPathEvaluator.EvaluateString(body, auth.LoginTokenJsonPath ?? "$.token");
            }
            catch (JsonException)
            {
                return new(false, url, (int)response.StatusCode, sw.ElapsedMilliseconds, "InvalidResponse", "Login response is not valid JSON.", null);
            }

            return string.IsNullOrEmpty(token)
                ? new(false, url, (int)response.StatusCode, sw.ElapsedMilliseconds, "TokenNotFound",
                    $"Login returned {(int)response.StatusCode} but no token was found at '{auth.LoginTokenJsonPath}'.", null)
                : new(true, url, (int)response.StatusCode, sw.ElapsedMilliseconds, null,
                    $"Login succeeded and a token was extracted from '{auth.LoginTokenJsonPath}'.", token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            sw.Stop();
            var error = HttpErrorClassifier.Classify(ex, url, timeout, cts.IsCancellationRequested);
            return new(false, url, null, sw.ElapsedMilliseconds, error.Kind, error.Message, null);
        }
    }

    /// <summary>Builds the same login request k6 sends: the configured headers (Content-Type defaults to JSON) and body.</summary>
    internal static HttpRequestMessage BuildRequest(string url, ResolvedAuth auth)
    {
        var headers = HeaderRules.BuildLoginHeaders(auth.LoginHeaders);
        var content = new StringContent(auth.LoginBody ?? "{}", Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(headers[HeaderRules.ContentType]);

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (name, value) in headers)
        {
            if (name == HeaderRules.ContentType)
            {
                continue;
            }
            if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Accept.Clear();
            }
            // Content-* headers belong on the content; everything else on the request.
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return request;
    }
}
