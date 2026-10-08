using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Common;
using PerfPilot.Core.Contracts;

namespace PerfPilot.Infrastructure.Http;

/// <summary>Downloads OpenAPI documents with HttpClient on the server, so target APIs never need CORS changes.</summary>
internal sealed class SpecFetcher(IHttpClientFactory httpClientFactory, ILogger<SpecFetcher> logger) : ISpecFetcher
{
    public async Task<FetchedSpec> FetchAsync(SpecFetchRequest request, CancellationToken ct)
    {
        var host = new Uri(request.Url).Host;
        var client = httpClientFactory.CreateClient(HttpClientNames.For(request.IgnoreTlsErrors));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(request.Timeout);
        var sw = Stopwatch.StartNew();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
            message.Headers.TryAddWithoutValidation("Accept", "application/json, application/yaml, application/x-yaml, text/yaml, */*");
            foreach (var (key, value) in request.Headers)
            {
                message.Headers.Remove(key);
                message.Headers.TryAddWithoutValidation(key, value);
            }

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (HttpErrorClassifier.ClassifyStatus(response.StatusCode, host) is { } statusError)
            {
                throw new ExternalCallException($"Could not download the OpenAPI document: {statusError.Message}", statusError.Kind);
            }
            if (response.Content.Headers.ContentLength is { } length && length > request.MaxBytes)
            {
                throw new ExternalCallException($"The OpenAPI document is {length / 1024 / 1024} MB, above the {request.MaxBytes / 1024 / 1024} MB limit.", "TooLarge");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var content = await ReadLimitedAsync(stream, request.MaxBytes, cts.Token);
            sw.Stop();
            logger.LogInformation("Downloaded OpenAPI document from {Url}: {Bytes} chars in {Ms} ms", request.Url, content.Length, sw.ElapsedMilliseconds);
            return new FetchedSpec(content, response.Content.Headers.ContentType?.MediaType, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            var error = HttpErrorClassifier.Classify(ex, request.Url, request.Timeout, cts.IsCancellationRequested);
            logger.LogWarning(ex, "Failed to download OpenAPI document from {Url}: {Kind}", request.Url, error.Kind);
            throw new ExternalCallException($"Could not download the OpenAPI document: {error.Message}", error.Kind, ex);
        }
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                throw new ExternalCallException($"The OpenAPI document exceeds the {maxBytes / 1024 / 1024} MB limit.", "TooLarge");
            }
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }
}
