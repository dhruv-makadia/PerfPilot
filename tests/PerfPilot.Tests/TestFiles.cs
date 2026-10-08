using System.Runtime.CompilerServices;

namespace PerfPilot.Tests;

internal static class TestFiles
{
    private static string Root([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    public static string FixturePath(string name) => Path.Combine(Root(), "Fixtures", name);

    public static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    public static string SnapshotPath(string name) => Path.Combine(Root(), "Snapshots", name);
}

/// <summary>
/// Minimal snapshot assertion. On mismatch (or a missing snapshot) the actual output is written next to the
/// snapshot as *.received.* and the test fails; review it and rename to *.verified.* to accept.
/// Set UPDATE_SNAPSHOTS=1 to overwrite verified snapshots in place.
/// </summary>
internal static class Snapshot
{
    public static void Match(string actual, string name, string extension = "js")
    {
        var verified = TestFiles.SnapshotPath($"{name}.verified.{extension}");
        var received = TestFiles.SnapshotPath($"{name}.received.{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(verified)!);
        var normalized = actual.Replace("\r\n", "\n");

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(verified, normalized);
            return;
        }

        if (!File.Exists(verified))
        {
            File.WriteAllText(received, normalized);
            Assert.Fail($"No verified snapshot for '{name}'. Review {received} and rename it to {Path.GetFileName(verified)}.");
        }

        var expected = File.ReadAllText(verified).Replace("\r\n", "\n");
        if (expected != normalized)
        {
            File.WriteAllText(received, normalized);
            Assert.Equal(expected, normalized);
        }
        else if (File.Exists(received))
        {
            File.Delete(received);
        }
    }
}
