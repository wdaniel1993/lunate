using System.Globalization;
using System.Text;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The text a dry run prints (spec: "Install output and <c>--dry-run</c>"): the identity read from
/// the archive, what the archive holds and the content kinds it would install. It writes nothing —
/// <see cref="PackageReader.Read"/> does not extract — and every number is invariant.
/// </summary>
public static class PackageReport
{
    public static string Describe(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);

        StringBuilder text = new();
        foreach (string line in Body(package, package.ArchivePath))
        {
            text.AppendLine(line);
        }

        text.AppendLine("dry-run: nothing was written");
        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// The same identity lines with the feed a real install read from and the scope it would write
    /// to: what <c>lunate install</c> prints before writing, and what a dry run prints in place of
    /// the archive path.
    /// </summary>
    public static IReadOnlyList<string> Identity(
        PackageInfo package,
        string source,
        string scope,
        string? worktree = null
    )
    {
        ArgumentNullException.ThrowIfNull(package);

        List<string> lines = [.. Body(package, source), Invariant($"scope: {scope}")];
        if (worktree is { Length: > 0 })
        {
            lines.Add(Invariant($"worktree: {worktree}"));
        }

        return lines;
    }

    private static IEnumerable<string> Body(PackageInfo package, string source)
    {
        yield return Invariant($"package: {package.Id} {package.Version}");
        yield return Invariant($"source: {source}");
        yield return Invariant($"sha512: {package.Sha512}");
        yield return Invariant(
            $"archive: {package.Files.Count} files, {package.ExpandedBytes} bytes expanded"
        );

        if (package.Extension is { } extension)
        {
            string entry = extension.Manifest.EntryAssembly is { } assembly
                ? Invariant($", entry {assembly}")
                : string.Empty;
            yield return Invariant(
                $"extension: {extension.Manifest.Id} {extension.Manifest.Version} (apiVersion {extension.Manifest.ApiVersion}{entry})"
            );
        }

        if (package.Skills.Count > 0)
        {
            yield return Invariant(
                $"skills: {string.Join(", ", package.Skills.Select(skill => skill.Name))}"
            );
        }

        if (package.IgnoredFolders.Count > 0)
        {
            yield return Invariant(
                $"ignored: {string.Join(", ", package.IgnoredFolders.Select(folder => folder + "/"))}"
            );
        }
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
