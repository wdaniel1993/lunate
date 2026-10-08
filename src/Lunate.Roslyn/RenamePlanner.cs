namespace Lunate.Roslyn;

/// <summary>One document's old and new text (line endings may differ).</summary>
internal readonly record struct RenameText(string File, string OldText, string NewText);

/// <summary>The aggregated rename plan produced by <see cref="RenamePlanner"/>.</summary>
internal sealed record RenamePlan(
    IReadOnlyList<RenameChange> Changes,
    int TotalFileCount,
    int TotalChangeCount,
    bool Truncated
);

/// <summary>
/// Builds the bounded per-file edit plan of a rename: the compare is line-by-line after EOL
/// normalization, entries are line number plus trimmed old and new text, file order is preserved,
/// and the entry cap keeps the first 500 while totals count every change.
/// </summary>
internal static class RenamePlanner
{
    internal const int MaxChangeEntries = 500;
    internal const int MaxLineLength = 120;

    public static RenamePlan Build(IEnumerable<RenameText> documents)
    {
        List<(string File, IReadOnlyList<RenameLineChange> Entries)> files = [];
        var total = 0;
        foreach (var document in documents)
        {
            var changes = LineChanges(document.OldText, document.NewText);
            if (changes.Count == 0)
            {
                continue;
            }

            total += changes.Count;
            files.Add((document.File, changes));
        }

        var remaining = MaxChangeEntries;
        List<RenameChange> retained = [];
        foreach (var (file, entries) in files)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, entries.Count);
            retained.Add(new RenameChange(file, entries.Take(take).ToArray()));
            remaining -= take;
        }

        return new RenamePlan(retained, files.Count, total, total > MaxChangeEntries);
    }

    internal static IReadOnlyList<RenameLineChange> LineChanges(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        List<RenameLineChange> changes = [];
        var count = Math.Max(oldLines.Length, newLines.Length);
        for (var index = 0; index < count; index++)
        {
            var oldLine = index < oldLines.Length ? oldLines[index] : string.Empty;
            var newLine = index < newLines.Length ? newLines[index] : string.Empty;
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                changes.Add(
                    new RenameLineChange(index + 1, Bound(oldLine.Trim()), Bound(newLine.Trim()))
                );
            }
        }

        return changes;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string Bound(string line) =>
        line.Length <= MaxLineLength ? line : string.Concat(line.AsSpan(0, MaxLineLength - 1), "…");
}
