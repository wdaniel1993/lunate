using System.Text.RegularExpressions;

namespace Lunate.Tui.Tests;

/// <summary>
/// <c>docs/guide.md</c>'s key table is where a reader learns what a key does; the catalog is where
/// the router learns it. These tests read the table from the page and the catalog through
/// <see cref="KeyBindingCatalog"/> — the same type the router uses — so neither can gain an entry
/// the other never learns. A row may list several keys for one action; the comparison is per key,
/// so an alternative key is not a second row.
/// </summary>
public sealed partial class GuideKeyTableTests
{
    private const string KeysStart = "<!-- keys:start -->";
    private const string KeysEnd = "<!-- keys:end -->";

    /// <summary>
    /// Keys the guide documents without a catalog binding, each for a reason the catalog cannot
    /// carry. A row that stops needing its exemption turns the stale-exemption test red.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exemptions = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["?"] =
            "the key-help overlay is gated on an empty idle prompt, a condition the catalog's "
            + "fixed contexts cannot express; the session handles it before the router",
        ["y"] = "the approval panel owns its letter keys; the session returns before the router",
        ["n"] = "the approval panel owns its letter keys; the session returns before the router",
        ["a"] = "the approval panel owns its letter keys; the session returns before the router",
        ["Ctrl+P"] =
            "deliberately unbound since #124: the model picker is Alt+P, and the guide lists "
            + "the absence on purpose",
    };

    [Fact]
    public void Every_catalog_binding_key_is_in_the_guide_key_table()
    {
        IReadOnlySet<string> catalog = CatalogKeys();
        IReadOnlySet<string> guide = GuideKeys();

        string[] missing =
        [
            .. catalog.Except(guide, StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];

        Assert.True(
            missing.Length == 0,
            "key(s) bound in src/Lunate.Tui/keys.json with no row in docs/guide.md's key table: "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Every_guide_key_table_key_names_a_binding_or_a_stated_exemption()
    {
        IReadOnlySet<string> catalog = CatalogKeys();
        IReadOnlySet<string> guide = GuideKeys();

        string[] unexplained =
        [
            .. guide
                .Except(catalog, StringComparer.Ordinal)
                .Where(key => !Exemptions.ContainsKey(key))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            unexplained.Length == 0,
            "key(s) in docs/guide.md's key table with no binding in src/Lunate.Tui/keys.json "
                + "and no stated exemption: "
                + string.Join(", ", unexplained)
        );
    }

    [Fact]
    public void Every_exemption_still_covers_a_guide_only_key()
    {
        IReadOnlySet<string> catalog = CatalogKeys();
        IReadOnlySet<string> guide = GuideKeys();

        string[] stale =
        [
            .. Exemptions
                .Keys.Where(key => !guide.Contains(key) || catalog.Contains(key))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            stale.Length == 0,
            "exemption(s) that no longer cover a guide-only key; remove or update them: "
                + string.Join(", ", stale)
        );
    }

    private static IReadOnlySet<string> CatalogKeys() =>
        KeyBindingCatalog
            .All.Select(binding => KeyPatternFormatter.Describe(binding.Key))
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> GuideKeys() =>
        GuideRows().SelectMany(row => row.Keys).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<GuideRow> GuideRows()
    {
        string path = Path.Combine(TestPaths.RepositoryRoot, "docs", "guide.md");
        Assert.True(File.Exists(path), $"the guide is missing: {path}");
        string text = File.ReadAllText(path);

        int start = text.IndexOf(KeysStart, StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            $"docs/guide.md has no '{KeysStart}' marker, so the key table cannot be found"
        );
        int end = text.IndexOf(KeysEnd, StringComparison.Ordinal);
        Assert.True(
            end > start,
            $"docs/guide.md has no '{KeysEnd}' marker after '{KeysStart}', so the key table "
                + "cannot be found"
        );

        string region = text[(start + KeysStart.Length)..end];
        List<GuideRow> rows = [];
        var header = false;
        foreach (string rawLine in region.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith('|'))
            {
                continue;
            }

            if (line.StartsWith("| Key |", StringComparison.Ordinal))
            {
                header = true;
                continue;
            }

            if (line.StartsWith("| --- |", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                header,
                $"docs/guide.md's keys region has a row before its '| Key | Action |' header: {line}"
            );
            string[] cells = line.Split('|');
            Assert.True(
                cells.Length >= 3,
                $"docs/guide.md's key table row has no action cell: {line}"
            );
            string[] keys =
            [
                .. BacktickedKey().Matches(cells[1]).Select(match => match.Groups[1].Value),
            ];
            Assert.True(
                keys.Length > 0,
                $"docs/guide.md's key table row has no backticked key: {line}"
            );
            Assert.False(
                string.IsNullOrWhiteSpace(cells[2]),
                $"docs/guide.md's key table row has no action text: {line}"
            );
            rows.Add(new GuideRow(keys));
        }

        Assert.True(header, "docs/guide.md's keys region has no '| Key | Action |' header");
        Assert.True(rows.Count > 0, "docs/guide.md's keys region has no key rows");
        return rows;
    }

    [GeneratedRegex(@"`([^`]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex BacktickedKey();

    private sealed record GuideRow(IReadOnlyList<string> Keys);
}
