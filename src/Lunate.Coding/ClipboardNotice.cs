using System.Globalization;

namespace Lunate.Coding;

/// <summary>
/// The one line <c>/copy</c> reports (design decision 2): a platform tool is reported as
/// <em>copied</em>, because its exit code is evidence; OSC 52 is reported as <em>sent</em>, because
/// the write cannot be confirmed. tmux needs <c>set-clipboard on</c> for OSC 52, and an oversized
/// payload may be truncated by the terminal — both are named once, in the same line.
/// </summary>
internal static class ClipboardNotice
{
    /// <summary>The answer <c>/copy</c> reports when no turn has completed; nothing was written.</summary>
    internal const string Nothing = "nothing to copy: no completed answer yet";

    /// <summary>The notice for one write: what happened, the tool or the sequence, the count, the caveats.</summary>
    internal static string Text(ClipboardCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        string characters = copy.Characters == 1 ? "character" : "characters";
        string count = copy.Characters.ToString("N0", CultureInfo.InvariantCulture);
        string notice =
            copy.Route == ClipboardRoute.PlatformTool
                ? FormattableString.Invariant($"copied {count} {characters} via {copy.Tool}")
                : FormattableString.Invariant(
                    $"sent {count} {characters} to the terminal clipboard (OSC 52)"
                );
        if (copy.Tmux)
        {
            notice += "; tmux requires set-clipboard on for this to work";
        }

        if (copy.MayBeTruncated)
        {
            notice += "; the terminal may truncate a copy this large";
        }

        return notice;
    }

    /// <summary>The teaching error when the write itself failed; the reason is the platform's.</summary>
    internal static string Failed(string reason) => "could not copy: " + reason;
}
