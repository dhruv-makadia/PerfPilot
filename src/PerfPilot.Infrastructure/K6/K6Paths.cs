using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PerfPilot.Core.Options;

namespace PerfPilot.Infrastructure.K6;

/// <summary>Resolves configured k6 paths against the application content root.</summary>
public sealed class K6Paths(IOptions<K6Options> options, IHostEnvironment host)
{
    /// <summary>
    /// The executable to start. A relative path that exists under the content root is resolved to it;
    /// otherwise the value is used as-is so that "k6" is looked up on PATH.
    /// </summary>
    public string BinaryPath
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(options.Value.BinaryPath) ? "k6" : options.Value.BinaryPath.Trim();
            if (Path.IsPathRooted(configured))
            {
                return configured;
            }
            var candidate = Path.GetFullPath(Path.Combine(host.ContentRootPath, configured));
            return File.Exists(candidate) ? candidate : configured;
        }
    }

    public string WorkDirectory
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(options.Value.WorkDirectory) ? "work" : options.Value.WorkDirectory.Trim();
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(host.ContentRootPath, configured));
        }
    }

    public string RunDirectory(Guid runId) => Path.Combine(WorkDirectory, "runs", runId.ToString("N"));
}
