using System.Text.RegularExpressions;

namespace Lunate.Roslyn;

/// <summary>Explains MSBuild build-host failures that name a missing SDK (ADR-0006 soft-fail).</summary>
internal static partial class SdkMismatch
{
    [GeneratedRegex(@"Requested SDK version:\s*(?<version>\S+)")]
    private static partial Regex RequestedVersion();

    [GeneratedRegex(@"global\.json file:\s*(?<path>[^\r\n]+)")]
    private static partial Regex GlobalJsonPath();

    /// <summary>The actionable explanation for an SDK-mismatch message, or null for other failures.</summary>
    public static string? Explain(string message)
    {
        if (
            !message.Contains(
                "compatible .NET SDK was not found",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        var versionMatch = RequestedVersion().Match(message);
        if (!versionMatch.Success)
        {
            return "no compatible .NET SDK was found; install the SDK the solution needs or update its global.json";
        }

        var version = versionMatch.Groups["version"].Value;
        var pathMatch = GlobalJsonPath().Match(message);
        var location = pathMatch.Success
            ? $" (pinned by {pathMatch.Groups["path"].Value.Trim()})"
            : string.Empty;
        return $"the solution pins .NET SDK {version}, which is not installed{location}; install that SDK or update global.json to an installed version";
    }
}
