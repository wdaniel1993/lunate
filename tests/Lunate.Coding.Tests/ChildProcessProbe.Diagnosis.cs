using System.Globalization;

namespace Lunate.Coding.Tests;

/// <summary>
/// The spawn probe's failure text: what the probe knows about a pid file that did not appear in
/// time. It lives next to <see cref="ChildProcessProbe"/> because that is the only caller, and it is
/// separate because the probe's loop is about pinning a process and this is about explaining one
/// that was never named.
/// </summary>
internal static partial class ChildProcessProbe
{
    /// <summary>
    /// The timeout, plus everything the probe knows about why no pid arrived: whether the file
    /// existed at all, what the command was doing, and whether the file landed somewhere else
    /// under the directory the command was given.
    /// </summary>
    private static string DescribeExpiry(
        string pidFile,
        TimeSpan spawnWindow,
        string? observedText,
        CommandUnderTest? command
    )
    {
        string fileState;
        if (observedText is { } text)
        {
            fileState = int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            )
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"the probe read pid {text} from the file but no live process carried that number"
                )
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"the probe read \"{text}\" from the file, which is not a pid"
                );
        }
        else if (File.Exists(pidFile))
        {
            fileState = "the file exists but the probe never read it";
        }
        else
        {
            fileState = "the file never appeared at the path the probe watched";
        }

        var parts = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"no pid was pinned from {pidFile} within {spawnWindow.TotalSeconds:0.###} seconds"
            ),
            fileState,
        };

        if (command is not null)
        {
            parts.Add(command.Describe());
            parts.Add(command.Locate(pidFile));
        }

        return string.Join("; ", parts);
    }
}
