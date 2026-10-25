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
        text.AppendLine(Invariant($"package: {package.Id} {package.Version}"));
        text.AppendLine(Invariant($"source: {package.ArchivePath}"));
        text.AppendLine(Invariant($"sha512: {package.Sha512}"));
        text.AppendLine(
            Invariant(
                $"archive: {package.Files.Count} files, {package.ExpandedBytes} bytes expanded"
            )
        );

        if (package.Extension is { } extension)
        {
            string entry = extension.Manifest.EntryAssembly is { } assembly
                ? Invariant($", entry {assembly}")
                : string.Empty;
            text.AppendLine(
                Invariant(
                    $"extension: {extension.Manifest.Id} {extension.Manifest.Version} (apiVersion {extension.Manifest.ApiVersion}{entry})"
                )
            );
        }

        if (package.Skills.Count > 0)
        {
            text.AppendLine(
                Invariant(
                    $"skills: {string.Join(", ", package.Skills.Select(skill => skill.Name))}"
                )
            );
        }

        if (package.IgnoredFolders.Count > 0)
        {
            text.AppendLine(
                Invariant(
                    $"ignored: {string.Join(", ", package.IgnoredFolders.Select(folder => folder + "/"))}"
                )
            );
        }

        text.AppendLine("dry-run: nothing was written");
        return text.ToString().ReplaceLineEndings("\n");
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
