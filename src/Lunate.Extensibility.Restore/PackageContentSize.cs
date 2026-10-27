using System.Globalization;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// What an install costs in bytes (spec: "Install output and <c>--dry-run</c>"): the total its
/// content adds up to, how that number is spelled, and the one decision about size this version
/// makes — a project install above ten mebibytes warns and names its largest entries, because a
/// project install is content the repository commits.
/// <para>
/// The threshold and the spelling live together here, so moving the budget is one edit and every
/// surface that prints a size says the same thing. Every number is formatted with the invariant
/// culture: CI runs under a non-English culture and a decimal comma in a size is a bug.
/// </para>
/// </summary>
internal static class PackageContentSize
{
    /// <summary>
    /// The size above which a project install warns: 10 MiB. It is not a limit — an install above it
    /// still completes — but the content is committed, so the number is worth saying out loud.
    /// </summary>
    public const long ProjectWarnThreshold = 10L * 1024 * 1024;

    /// <summary>How many of the largest entries a warning names.</summary>
    public const int LargestCount = 3;

    /// <summary>
    /// A byte count as one short human-readable string: whole bytes below a kibibyte, then KiB, MiB
    /// and GiB with at most one decimal and no trailing <c>.0</c> (<c>512 B</c>, <c>1.5 KiB</c>,
    /// <c>10 MiB</c>). Invariant, always.
    /// </summary>
    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        string[] units = ["KiB", "MiB", "GiB", "TiB"];
        double value = bytes / 1024d;
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>The content's total: every recorded file's size, summed.</summary>
    public static long Total(IReadOnlyList<InstalledFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var total = 0L;
        foreach (InstalledFile file in files)
        {
            total += file.Size;
        }

        return total;
    }

    /// <summary>
    /// Whether a size is worth a warning at this scope: only a project install's content is
    /// committed, so only that scope warns, and only above the threshold.
    /// </summary>
    public static bool WarnsFor(PackageScope scope, long bytes) =>
        scope is PackageScope.Project && bytes > ProjectWarnThreshold;

    /// <summary>
    /// The warning a project install prints, naming the largest entries — or null when there is
    /// nothing to say. The decision is pure: the scope and the recorded files, nothing else.
    /// </summary>
    public static string? Warning(PackageScope scope, IReadOnlyList<InstalledFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        long total = Total(files);
        if (!WarnsFor(scope, total))
        {
            return null;
        }

        IEnumerable<string> largest = files
            .OrderByDescending(file => file.Size)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .Take(LargestCount)
            .Select(file =>
                string.Create(CultureInfo.InvariantCulture, $"'{file.Path}' ({Format(file.Size)})")
            );

        return string.Create(
            CultureInfo.InvariantCulture,
            $"this project install writes {Format(total)} of content, above the {Format(ProjectWarnThreshold)} this version warns at; the largest entries are {string.Join(", ", largest)}. A project install commits its content, so keep it only if the repository wants those bytes."
        );
    }
}
