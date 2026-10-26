using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// What <c>lunate uninstall</c> prints (spec: "Uninstall", "Install output and <c>--dry-run</c>"):
/// the package it removed, the scope it came from, how many recorded files went, one line for every
/// directory it kept because the package did not write everything in it, and what happened to the
/// record. The JSON object carries the same fields plus the lists themselves.
/// </summary>
internal static class UninstallView
{
    internal static string Text(PackageUninstallResult result, string storeRoot)
    {
        ArgumentNullException.ThrowIfNull(result);

        string scopeRoot = ScopeRoot(result, storeRoot);
        List<string> lines =
        [
            Invariant($"package: {result.Record.Package.Id} {result.Record.Package.Version}"),
            Invariant($"scope: {ScopeText(result.Record.Scope)}"),
        ];
        if (result.Record.Worktree is { Length: > 0 } worktree)
        {
            lines.Add(Invariant($"worktree: {worktree}"));
        }

        lines.Add(Invariant($"removed: {result.RemovedFiles.Count} files under '{scopeRoot}'"));
        foreach (string directory in result.KeptDirectories)
        {
            string[] files = [.. result.LeftFiles.Where(file => IsUnder(file, directory))];
            lines.Add(
                files.Length == 1
                    ? Invariant(
                        $"left: '{directory}' is not empty — 1 file the package did not write ('{files[0]}')"
                    )
                    : Invariant(
                        $"left: '{directory}' is not empty — {files.Length} files the package did not write ({string.Join(", ", files.Select(file => "'" + file + "'"))})"
                    )
            );
        }

        lines.Add(
            result.RecordRemoved
                ? Invariant($"record: '{result.RecordPath}' removed")
                : Invariant($"record: '{result.RecordPath}' kept")
        );
        if (result.Forced)
        {
            lines.Add("forced: a changed file or an edited record was removed anyway");
        }

        return Lines(lines);
    }

    internal static string Json(PackageUninstallResult result, string storeRoot)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = result.Record.Package.Id,
            ["version"] = result.Record.Package.Version,
            ["scope"] = ScopeText(result.Record.Scope),
            ["worktree"] = result.Record.Worktree,
            ["record"] = result.RecordPath,
            ["recordRemoved"] = result.RecordRemoved,
            ["forced"] = result.Forced,
            ["removed"] = Strings(result.RemovedFiles),
            ["left"] = Strings(result.LeftFiles),
            ["directories"] = Strings(result.KeptDirectories),
        }.ToJsonString();
    }

    /// <summary>The directory the record's paths are relative to: the worktree, or the state root.</summary>
    private static string ScopeRoot(PackageUninstallResult result, string storeRoot) =>
        result.Record.Scope == PackageScope.Project
        && result.Record.Worktree is { Length: > 0 } root
            ? root
            : storeRoot;

    /// <summary>
    /// What a forced removal of a package whose record could not be read did (spec: "Uninstall"):
    /// every path it removed, and the two places only a readable record could have searched.
    /// </summary>
    internal static string Text(PackageBrokenUninstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> lines =
        [
            Invariant($"package: {result.PackageId} (the record could not be read)"),
            Invariant($"record: '{result.RecordPath}' removed"),
            Invariant($"removed: {result.RemovedFiles.Count} files"),
        ];
        foreach (string file in result.RemovedFiles)
        {
            lines.Add(Invariant($"removed file: '{file}'"));
        }

        foreach (string directory in result.RemovedDirectories)
        {
            lines.Add(Invariant($"removed directory: '{directory}'"));
        }

        lines.Add(
            Invariant(
                $"not known: '{result.UnknownPaths[0]}' (the skill directories) and '{result.UnknownPaths[1]}' (an extension id unlike the package id) — only a readable record could have named them"
            )
        );
        lines.Add(
            "forced: the record could not be read, so the package id named the paths it could"
        );
        return Lines(lines);
    }

    internal static string Json(PackageBrokenUninstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = result.PackageId,
            ["version"] = null,
            ["scope"] = null,
            ["worktree"] = null,
            ["record"] = result.RecordPath,
            ["recordRemoved"] = true,
            ["recordUnreadable"] = result.Problem,
            ["forced"] = true,
            ["removed"] = Strings(result.RemovedFiles),
            ["directories"] = Strings(result.RemovedDirectories),
            ["left"] = new System.Text.Json.Nodes.JsonArray(),
            ["notKnown"] = Strings(result.UnknownPaths),
        }.ToJsonString();
    }

    private static string ScopeText(PackageScope scope) =>
        scope == PackageScope.Project ? "project" : "user";

    private static bool IsUnder(string path, string directory)
    {
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return path.StartsWith(
            Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            comparison
        );
    }

    private static System.Text.Json.Nodes.JsonArray Strings(IReadOnlyList<string> values)
    {
        var array = new System.Text.Json.Nodes.JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string Lines(IReadOnlyList<string> lines) =>
        string.Join('\n', lines).ReplaceLineEndings("\n") + "\n";

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
