namespace Lunate.Tui.Tests;

/// <summary>
/// The guide's key table is a generated region (<c>scripts/docs-sync</c>) driven by
/// <c>keys.json</c>'s <c>guideRows</c>, whose rows are pinned to the catalog by
/// <see cref="KeysFileTests"/>. This file closes the side nothing else guards in a test run: the
/// docs-sync region check runs only in the docs profile, so a keys.json change that forgets to
/// regenerate <c>docs/guide.md</c> is noticed on CI only by a docs-only pull request. Here the
/// page's key cells are checked against <see cref="KeyBindingCatalog"/> itself, so a binding can
/// neither lose its doc row nor be shadowed by a hand edit of the region.
/// </summary>
/// <remarks>
/// This is the suite's one repository read of the docs tree, allowlisted in
/// <c>scripts/no-docs-references.sh</c>. A guide-table row may document a key the router does not
/// carry, and every such row is exempted here, named with its reason rather than worked around
/// (issue #267): the approval panel's letter keys (<c>y</c>, <c>n</c>, <c>a</c>), which the panel
/// owns before the router sees them; <c>?</c>, gated on an empty idle prompt — a condition the
/// catalog's context flags cannot express — and on the approval legend; and <c>Ctrl+P</c>,
/// deliberately unbound and listed so a reader does not have to guess. The first red run of this
/// test surfaced the <c>?</c> row, which the issue's two exemptions did not name.
/// </remarks>
public sealed class GuideKeyTableTests
{
    /// <summary>
    /// The keys a guide-table row may document without a catalog binding. A fourth kind — a row
    /// for a key no binding has and nothing exempts — is what this file exists to refuse. The
    /// letters are the approval prompt's; <c>?</c> fires on an empty idle prompt and in the
    /// approval legend, neither of which the context flags can express; <c>Ctrl+P</c> is unbound
    /// on purpose (the model picker is <c>Alt+P</c>).
    /// </summary>
    private static readonly string[] ExemptUnboundKeys = ["y", "n", "a", "?", "Ctrl+P"];

    private static readonly string[] PageKeys = GuideKeyTableKeys();

    /// <summary>
    /// Every binding the router will answer to must also be in the reader-facing table: a binding
    /// added without a doc row is a key the guide does not admit having.
    /// </summary>
    [Fact]
    public void Every_catalog_binding_appears_in_the_guide_key_table()
    {
        foreach (KeyBinding binding in KeyBindingCatalog.All)
        {
            string spelled = KeyPatternFormatter.Describe(binding.Key);
            Assert.True(
                PageKeys.Contains(spelled, StringComparer.Ordinal),
                $"the guide key table has no row for the binding '{binding.Description}' ({spelled}); "
                    + "add its row to keys.json's guideRows and run scripts/docs-sync"
            );
        }
    }

    /// <summary>
    /// A key in the table that nothing routes is either one of the exempt rows or a hand edit of a
    /// generated region.
    /// </summary>
    [Fact]
    public void The_guide_tables_unbound_keys_are_only_the_declared_exemptions()
    {
        string[] catalogKeys =
        [
            .. KeyBindingCatalog.All.Select(binding => KeyPatternFormatter.Describe(binding.Key)),
        ];
        string[] unbound = PageKeys
            .Distinct(StringComparer.Ordinal)
            .Where(key => !catalogKeys.Contains(key, StringComparer.Ordinal))
            .ToArray();

        Assert.All(
            unbound,
            key =>
                Assert.True(
                    ExemptUnboundKeys.Contains(key, StringComparer.Ordinal),
                    $"the guide table keys '{key}', which matches no KeyBindingCatalog binding and is "
                        + "not one of the exempt rows (the approval letters y, n, a; the help key ?, "
                        + "gated on an empty idle prompt the context flags cannot express; and the "
                        + "deliberately-unbound Ctrl+P); if it is meant to route, add it to keys.json "
                        + "and run scripts/docs-sync, otherwise remove the row from docs/guide.md"
                )
        );
    }

    /// <summary>
    /// The table's key cell is the first column, between the region markers; the markers are the
    /// shape the page carries a generated region in, so their disappearance is a failure, not an
    /// empty pass.
    /// </summary>
    private static string[] GuideKeyTableKeys()
    {
        string page = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "guide.md"));
        string[] lines = page.Split('\n');
        int[] starts =
        [
            .. lines
                .Select(line => line.Trim())
                .Select((trimmed, index) => (trimmed, index))
                .Where(pair => pair.trimmed == "<!-- keys:start -->")
                .Select(pair => pair.index),
        ];
        int[] ends =
        [
            .. lines
                .Select(line => line.Trim())
                .Select((trimmed, index) => (trimmed, index))
                .Where(pair => pair.trimmed == "<!-- keys:end -->")
                .Select(pair => pair.index),
        ];
        Assert.True(
            starts.Length == 1 && ends.Length == 1 && starts[0] < ends[0],
            $"docs/guide.md must carry exactly one '<!-- keys:start -->' above exactly one "
                + $"'<!-- keys:end -->' (found {starts.Length} and {ends.Length}); the markers say "
                + "where the generated key table goes"
        );

        string[] rows =
        [
            .. lines[(starts[0] + 1)..ends[0]]
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal)),
        ];
        Assert.True(
            rows.Length > 2 && rows[0] == "| Key | Action |" && rows[1] == "| --- | --- |",
            "the region between the keys markers is not the generated table shape; run scripts/docs-sync"
        );

        string[] keys = [.. rows[2..].SelectMany(KeyCellKeys)];
        Assert.True(
            keys.Length > 0,
            "the guide key table holds no key cell; the table is empty and no key is documented"
        );
        return keys;
    }

    /// <summary>A key cell spells its keys as the help overlay does, backticked and ' / '-joined.</summary>
    private static string[] KeyCellKeys(string row)
    {
        string[] cells = row.Trim().Trim('|').Split(" | ");
        Assert.True(
            cells.Length == 2,
            $"the guide row '{row}' is not a two-column table row; the key table is fixed-shaped"
        );
        return cells[0]
            .Split(" / ")
            .Select(key => key.Trim().Trim('`'))
            .Where(key => key.Length > 0)
            .ToArray();
    }
}
