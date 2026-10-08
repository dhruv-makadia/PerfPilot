using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Options;

namespace PerfPilot.Infrastructure.Http;

/// <summary>Checks an environment from the PerfPilot server: base URL, OpenAPI URL and login flow.</summary>
internal sealed class ConnectionTester(
    IHttpClientFactory httpClientFactory,
    LoginClient loginClient,
    IOptions<ConnectionTestOptions> options,
    ILogger<ConnectionTester> logger) : IConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(ConnectionTestContext context, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));
        var checks = new List<ConnectionCheck>();
        var auth = context.Auth;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (auth.Type == AuthType.LoginFlow)
        {
            var login = await loginClient.TryLoginAsync(context.BaseUrl, auth, context.IgnoreTlsErrors, timeout, ct);
            checks.Add(new ConnectionCheck("Login flow", login.Url, login.Success ? CheckSeverity.Ok : CheckSeverity.Error,
                login.StatusCode, login.LatencyMs, login.ErrorKind, login.Message));
            if (login.Token is not null)
            {
                headers[auth.LoginTokenHeaderName] = auth.LoginTokenPrefix + login.Token;
            }
        }
        else if (auth.Type == AuthType.BearerToken && auth.Secret is not null)
        {
            headers["Authorization"] = "Bearer " + auth.Secret;
        }
        else if (auth.Type == AuthType.ApiKey && auth.Secret is not null && auth.ApiKeyHeaderName is not null)
        {
            headers[auth.ApiKeyHeaderName] = auth.Secret;
        }

        checks.Insert(0, await ProbeAsync("Base URL", context.BaseUrl, headers, context.IgnoreTlsErrors, timeout, isSpec: false, ct));
        if (!string.IsNullOrWhiteSpace(context.OpenApiUrl))
        {
            checks.Insert(1, await ProbeAsync("OpenAPI spec", context.OpenApiUrl, headers, context.IgnoreTlsErrors, timeout, isSpec: true, ct));
        }

        var success = checks.All(c => c.Severity != CheckSeverity.Error);
        logger.LogInformation("Connection test for {BaseUrl}: {Result} ({Checks})", context.BaseUrl, success ? "ok" : "failed",
            string.Join(", ", checks.Select(c => $"{c.Name}={c.Severity}")));
        return new ConnectionTestResult(success, checks);
    }

    private async Task<ConnectionCheck> ProbeAsync(string name, string url, Dictionary<string, string> headers, bool ignoreTls, TimeSpan timeout, bool isSpec, CancellationToken ct)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        var client = httpClientFactory.CreateClient(HttpClientNames.For(ignoreTls));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            string? body = null;
            if (isSpec && response.IsSuccessStatusCode)
            {
                body = await response.Content.ReadAsStringAsync(cts.Token);
            }
            sw.Stop();
            var status = (int)response.StatusCode;
            var latency = sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode || status is >= 300 and < 400)
            {
                if (isSpec && body is not null && !LooksLikeOpenApi(body))
                {
                    return new(name, url, CheckSeverity.Warning, status, latency, "NotOpenApi",
                        $"Reachable ({status}, {latency} ms) but the response does not look like an OpenAPI/Swagger document.");
                }
                return new(name, url, CheckSeverity.Ok, status, latency, null,
                    isSpec ? $"OpenAPI document reachable ({status}, {latency} ms)." : $"Reachable ({status}, {latency} ms).");
            }

            var error = HttpErrorClassifier.ClassifyStatus(response.StatusCode, host)!;
            // For the base URL, a 404/405 on "/" just means there's no route at the root — the host is reachable.
            var severity = !isSpec && status is 404 or 405
                ? CheckSeverity.Warning
                : !isSpec && status >= 500 ? CheckSeverity.Warning : CheckSeverity.Error;
            var message = !isSpec && status is 404 or 405
                ? $"Host reachable ({latency} ms) but the base URL itself returned {status}. This is normal if the API has no route at its root."
                : error.Message;
            return new(name, url, severity, status, latency, error.Kind, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            sw.Stop();
            var error = HttpErrorClassifier.Classify(ex, url, timeout, cts.IsCancellationRequested);
            return new(name, url, CheckSeverity.Error, null, sw.ElapsedMilliseconds, error.Kind, error.Message);
        }
    }

    private static bool LooksLikeOpenApi(string body)
    {
        var head = body.Length > 4096 ? body[..4096] : body;
        return head.Contains("openapi", StringComparison.OrdinalIgnoreCase) || head.Contains("swagger", StringComparison.OrdinalIgnoreCase);
    }
}
