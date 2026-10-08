using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PerfPilot.Infrastructure.K6;

public sealed record K6StartInfo(
    string BinaryPath,
    string WorkingDirectory,
    string ScriptFile,
    string MetricsFile,
    string SummaryFile,
    string LogFile,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

/// <summary>
/// A running <c>k6 run</c> child process. k6's REST API is bound to a private loopback port so the run
/// can be stopped gracefully (k6 still writes its summary), with a process-tree kill as the fallback.
/// </summary>
public sealed class K6Process : IAsyncDisposable
{
    private const int MaxRetainedLines = 300;

    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly Queue<string> _recentLines = new();
    private readonly object _logLock = new();
    private readonly ILogger _logger;

    private K6Process(Process process, StreamWriter log, int apiPort, ILogger logger)
    {
        _process = process;
        _log = log;
        ApiPort = apiPort;
        _logger = logger;
    }

    public int ApiPort { get; }
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public Task ExitTask { get; private set; } = Task.CompletedTask;
    public int ExitCode => _process.ExitCode;

    public static K6Process Start(K6StartInfo info, ILogger logger)
    {
        var port = GetFreeLoopbackPort();
        var psi = new ProcessStartInfo
        {
            FileName = info.BinaryPath,
            WorkingDirectory = info.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[]
        {
            "run",
            "--out", $"json={info.MetricsFile}",
            "--summary-export", info.SummaryFile,
            "--address", $"127.0.0.1:{port}",
            "--no-color",
            "--quiet",
            info.ScriptFile,
        })
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["K6_NO_USAGE_REPORT"] = "true";
        foreach (var (key, value) in info.EnvironmentVariables)
        {
            psi.Environment[key] = value;
        }

        var log = new StreamWriter(new FileStream(Path.Combine(info.WorkingDirectory, info.LogFile), FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true,
        };
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var k6 = new K6Process(process, log, port, logger);
        process.OutputDataReceived += (_, e) => k6.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => k6.OnLine(e.Data);

        try
        {
            process.Start();
        }
        catch
        {
            log.Dispose();
            process.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        k6.ExitTask = process.WaitForExitAsync();
        logger.LogInformation("Started k6 (pid {Pid}, REST API port {Port}) in {Dir}", process.Id, port, info.WorkingDirectory);
        return k6;
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (_logLock)
        {
            _log.WriteLine(line);
            _recentLines.Enqueue(line);
            while (_recentLines.Count > MaxRetainedLines)
            {
                _recentLines.Dequeue();
            }
        }
    }

    public IReadOnlyList<string> RecentOutput()
    {
        lock (_logLock)
        {
            return _recentLines.ToList();
        }
    }

    /// <summary>Asks k6 to stop through its REST API, then kills the process tree if it doesn't exit in time.</summary>
    public async Task StopAsync(HttpClient http, TimeSpan gracefulTimeout)
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"http://127.0.0.1:{ApiPort}/v1/status")
            {
                Content = new StringContent("""{"data":{"type":"status","id":"default","attributes":{"stopped":true}}}""", Encoding.UTF8, "application/json"),
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await http.SendAsync(request, cts.Token);
            _logger.LogInformation("Requested graceful stop of k6 pid {Pid}: HTTP {Status}", _process.Id, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Graceful stop request to k6 pid {Pid} failed", _process.Id);
        }

        var exited = await Task.WhenAny(ExitTask, Task.Delay(gracefulTimeout)) == ExitTask;
        if (!exited)
        {
            Kill();
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _logger.LogWarning("Killing k6 process tree (pid {Pid})", _process.Id);
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public async ValueTask DisposeAsync()
    {
        Kill();
        try
        {
            await ExitTask.WaitAsync(TimeSpan.FromSeconds(10));
            // WaitForExit() without timeout flushes the redirected output streams.
            _process.WaitForExit();
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("k6 pid {Pid} did not exit after kill", _process.Id);
        }
        lock (_logLock)
        {
            _log.Dispose();
        }
        _process.Dispose();
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
