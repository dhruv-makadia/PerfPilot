using System.Text;

namespace PerfPilot.Infrastructure.K6;

/// <summary>Follows a file that k6 is appending JSON lines to, invoking a callback per complete line.</summary>
public static class K6OutputTailer
{
    public static async Task TailAsync(string path, Action<string> onLine, Func<bool> isWriterDone, CancellationToken ct, int pollMs = 250)
    {
        while (!File.Exists(path))
        {
            if (isWriterDone())
            {
                if (!File.Exists(path))
                {
                    return;
                }
                break;
            }
            await Task.Delay(pollMs, ct);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[1 << 16];
        var pending = new StringBuilder();

        while (true)
        {
            // Check before reading so that, once the writer has exited, we drain everything it wrote and then stop.
            var writerDone = isWriterDone();
            var read = await reader.ReadAsync(buffer, ct);
            if (read > 0)
            {
                Emit(buffer.AsSpan(0, read), pending, onLine);
                continue;
            }
            if (writerDone)
            {
                if (pending.Length > 0)
                {
                    onLine(pending.ToString().TrimEnd('\r'));
                }
                return;
            }
            await Task.Delay(pollMs, ct);
        }
    }

    private static void Emit(ReadOnlySpan<char> chunk, StringBuilder pending, Action<string> onLine)
    {
        while (true)
        {
            var newline = chunk.IndexOf('\n');
            if (newline < 0)
            {
                pending.Append(chunk);
                return;
            }
            pending.Append(chunk[..newline]);
            var line = pending.ToString().TrimEnd('\r');
            pending.Clear();
            if (line.Length > 0)
            {
                onLine(line);
            }
            chunk = chunk[(newline + 1)..];
        }
    }
}
