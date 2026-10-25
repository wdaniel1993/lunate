namespace Lunate.Coding.Tests;

/// <summary>
/// The one-state-root rule, as a source scan: outside <c>LunateHome</c>, no production file may
/// resolve the user profile or name the <c>.lunate</c> directory itself. A second resolution site is
/// how a process pointed at its own root silently writes back into the real profile — the failure
/// the root exists to prevent — and a <c>.lunate</c> literal is the same bug in the other direction.
/// <para>
/// The allow-list is deliberately per file and per kind: a new <c>UserProfile</c> read in an
/// allow-listed NuGet file is still a new resolution site and fails. Each entry needs a reason;
/// <see cref="HomeResolutionTests.Every_allowed_site_carries_a_reason"/> enforces it.
/// </para>
/// </summary>
internal static class HomeResolutionChecker
{
    /// <summary>The one file that is allowed to resolve the profile: it is the rule.</summary>
    internal const string StateRootFile = "src/Lunate.Ai/LunateHome.cs";

    internal enum Resolution
    {
        /// <summary><c>Environment.SpecialFolder.UserProfile</c>.</summary>
        UserProfile,

        /// <summary><c>GetEnvironmentVariable("HOME")</c> or <c>("USERPROFILE")</c>.</summary>
        HomeVariable,

        /// <summary>A quoted <c>".lunate"</c> path segment.</summary>
        LunateDirectory,
    }

    internal sealed record AllowedSite(string File, Resolution Resolution, string Reason);

    internal static IReadOnlyList<AllowedSite> AllowedSites { get; } =
    [
        // NuGet's default global packages folder: content-addressed package storage the SDK shares,
        // not Lunate state. Moving it would make a sandbox restore into a second package cache.
        new(
            "src/Lunate.Extensibility.Restore/NuGetRestoreEngine.cs",
            Resolution.UserProfile,
            "NuGet's default global packages folder (`~/.nuget/packages`), shared with the SDK"
        ),
        // The same NuGet default, read by the one type that turns a build's package-relative asset
        // into a path: the compile and the load must agree on the folder, so the rule lives there
        // rather than twice (it moved here with SingleFileExtensionBuilder's copy).
        new(
            "src/Lunate.Extensibility/SingleFilePackages.cs",
            Resolution.UserProfile,
            "the same NuGet default (`~/.nuget/packages`), read for a compile and for a load's assets"
        ),
        // Presentation: `~` shortening of whatever path is being printed. It reads no state and
        // writes none, and the TUI assembly deliberately does not depend on Lunate.Ai.
        new(
            "src/Lunate.Tui/ToolBlocks/PathText.cs",
            Resolution.UserProfile,
            "display only: shortens a path under the real home to `~`; resolves no state"
        ),
        // Project-scoped directories: a `.lunate` inside the workspace, owned by the repository,
        // which is exactly where a store must not be — the guard's point, not a counter-example.
        new(
            "src/Lunate.Extensibility/ExtensionLoader.cs",
            Resolution.LunateDirectory,
            "project-scoped: the worktree's own `.lunate/extensions` directory"
        ),
        new(
            "src/Lunate.Coding/McpComposition.cs",
            Resolution.LunateDirectory,
            "project-scoped: the worktree's `.lunate/mcp.json`, a file the repository owns"
        ),
        new(
            "src/Lunate.Coding/SkillScanner.cs",
            Resolution.LunateDirectory,
            "project-scoped: `.lunate` is one of the worktree skill-root conventions"
        ),
        new(
            "src/Lunate.Extensibility.Testing/ExtensionTestHost.cs",
            Resolution.LunateDirectory,
            "test kit: a temporary worktree's project extension directory"
        ),
    ];

    /// <summary>The violations, as <c>path:line: detail</c>, in file order.</summary>
    internal static IReadOnlyList<string> FindViolations(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var violations = new List<string>();
        foreach (string file in SourceFiles(repositoryRoot))
        {
            string relative = Path.GetRelativePath(repositoryRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relative, StateRootFile, StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (Classify(lines[index]) is not { } resolution)
                {
                    continue;
                }

                if (
                    AllowedSites.Any(site =>
                        site.Resolution == resolution
                        && string.Equals(site.File, relative, StringComparison.Ordinal)
                    )
                )
                {
                    continue;
                }

                violations.Add(
                    $"{relative}:{index + 1}: {resolution} resolves the profile or names the store root directly; use LunateHome"
                );
            }
        }

        return violations;
    }

    /// <summary>Every <c>.cs</c> file under <c>src/</c>, without build output.</summary>
    internal static IReadOnlyList<string> SourceFiles(string repositoryRoot)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        return
        [
            .. Directory
                .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool IsBuildOutput(string file)
    {
        string[] segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("obj", StringComparer.Ordinal)
            || segments.Contains("bin", StringComparer.Ordinal);
    }

    private static Resolution? Classify(string line)
    {
        if (line.Contains("Environment.SpecialFolder.UserProfile", StringComparison.Ordinal))
        {
            return Resolution.UserProfile;
        }

        if (
            line.Contains("GetEnvironmentVariable(\"HOME\")", StringComparison.Ordinal)
            || line.Contains("GetEnvironmentVariable(\"USERPROFILE\")", StringComparison.Ordinal)
        )
        {
            return Resolution.HomeVariable;
        }

        return line.Contains("\".lunate\"", StringComparison.Ordinal)
            ? Resolution.LunateDirectory
            : null;
    }
}
