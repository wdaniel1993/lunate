using System.Globalization;
using System.Runtime.InteropServices;

namespace Lunate.TestSupport;

/// <summary>
/// The machine facts a silent child failure is usually blamed on: how many cores the suite is
/// sharing, how much room the temp disk has, and what the runner's own environment looks like.
/// Printed with a failed restore so the next reader does not have to guess what the box was doing.
/// </summary>
internal static class ChildProcessDiagnostics
{
    public static string Describe(string root)
    {
        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"processors: {Environment.ProcessorCount}"
            ),
            string.Create(CultureInfo.InvariantCulture, $"os: {RuntimeInformation.OSDescription}"),
        ];

        try
        {
            string? volume = Path.GetPathRoot(Path.GetFullPath(root));
            if (!string.IsNullOrEmpty(volume))
            {
                DriveInfo drive = new(volume);
                lines.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"free space on {volume}: {drive.AvailableFreeSpace / (1024 * 1024)} MiB"
                    )
                );
            }
        }
        catch (Exception exception)
            when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            lines.Add("free space: unknown");
        }

        Dictionary<string, string?> child = FixtureRestore.ChildEnvironment(root);
        foreach (
            string name in new[]
            {
                "HOME",
                "DOTNET_CLI_HOME",
                "DOTNET_ROOT",
                "DOTNET_HOST_PATH",
                "NUGET_PACKAGES",
                "NUGET_HTTP_CACHE_PATH",
                "TMPDIR",
            }
        )
        {
            // The child's own value wins: this report explains a restore failure, so it must show
            // the folders that child used, not the ones the test host happens to inherit.
            string? value = child.TryGetValue(name, out string? own)
                ? own
                : Environment.GetEnvironmentVariable(name);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={value ?? "<unset>"}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
