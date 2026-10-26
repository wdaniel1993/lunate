namespace Lunate.Tui.Tests;

/// <summary>
/// <c>docs/guide.md</c>'s key table against <see cref="KeyBindingCatalog"/>, in both directions:
/// catalog→doc catches a binding nobody documented, doc→catalog catches a row documenting a key
/// nothing routes. Only the Key column counts, merged cells are read key by key, and the
/// exemptions are a named table with a reason each — adding one is a spec change, not a test edit.
/// </summary>
public sealed class GuideKeyTableTests
{
    /// <summary>
    /// Table keys allowed to have no catalog row. Each carries the reason it is allowed and the
    /// behaviour behind it; the requirement names this list, so a new entry here is a spec change.
    /// </summary>
    private static readonly (string Key, string Reason)[] Exemptions =
    [
        (
            "y",
            "the approval panel owns its keys; the session returns before the router while a prompt is pending"
        ),
        (
            "n",
            "the approval panel owns its keys; the session returns before the router while a prompt is pending"
        ),
        (
            "a",
            "the approval panel owns its keys; the session returns before the router while a prompt is pending"
        ),
        (
            "?",
            "gated on an empty idle prompt, a condition the context flags cannot express; in an approval prompt it shows the panel's key legend, so the row has behaviour behind it"
        ),
        ("Ctrl+P", "deliberately unbound since #124; documented so a reader knows it does nothing"),
    ];

    private static readonly string Guide = GuidePath();

    [Fact]
    public void Every_catalog_key_has_a_guide_row()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(ReadGuide());

        Assert.Empty(Gaps(rows));
    }

    [Fact]
    public void Every_guide_key_has_a_catalog_row_or_a_stated_exemption()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(ReadGuide());

        Assert.Empty(Ghosts(rows));
    }

    [Fact]
    public void The_exemptions_are_exact_and_still_present()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(ReadGuide());
        var documented = rows.SelectMany(row => row.Keys).ToHashSet(StringComparer.Ordinal);

        foreach ((string key, string reason) in Exemptions)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(reason),
                $"the exemption for '{key}' has no reason"
            );
            Assert.True(
                documented.Contains(key),
                $"the exemption for '{key}' is stale: the table no longer documents it"
            );
        }
    }

    [Fact]
    public void Merged_cells_are_read_key_by_key()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(
            """
            **Key bindings (one meaning each)**

            | Key | Action |
            | --- | --- |
            | `Ctrl+A` / `Ctrl+E` | Move to the start / end of the line |
            | `Alt+Enter` / `Shift+Enter` / `Ctrl+J` / `Ctrl+Enter` | New line |
            """
        );

        Assert.Equal(["Ctrl+A", "Ctrl+E"], rows[0].Keys);
        Assert.Equal(["Alt+Enter", "Shift+Enter", "Ctrl+J", "Ctrl+Enter"], rows[1].Keys);
    }

    [Fact]
    public void A_key_named_only_in_an_action_cell_counts_for_nothing()
    {
        // The newline row loses Ctrl+J from its Key cell while the prose still names it: the gap
        // must still be reported, because a mention is not a documented binding.
        string guide = ReadGuide()
            .Replace(
                "`Alt+Enter` / `Shift+Enter` / `Ctrl+J` / `Ctrl+Enter`",
                "`Alt+Enter` / `Shift+Enter` / `Ctrl+Enter`"
            );
        Assert.Contains("`Ctrl+J`", guide, StringComparison.Ordinal);

        string[] gaps = Gaps(GuideKeyTable.Parse(guide));

        Assert.Contains(
            "KeyBindingCatalog's 'Ctrl+J' (Insert a newline) has no docs/guide.md row",
            gaps
        );
    }

    [Fact]
    public void A_table_key_with_no_catalog_row_names_the_key_the_direction_and_the_row()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(
            """
            **Key bindings (one meaning each)**

            | Key | Action |
            | --- | --- |
            | `Alt+R` | Reticulate the splines |
            """
        );

        string ghost = Assert.Single(Ghosts(rows));

        Assert.Equal(
            "docs/guide.md documents 'Alt+R' and no catalog row has it (row: '`Alt+R`' — Reticulate the splines)",
            ghost
        );
    }

    [Theory]
    [InlineData("", "has no '**Key bindings (one meaning each)**' heading")]
    [InlineData(
        "**Key bindings (one meaning each)**\n\nNo table here.\n",
        "has no '| Key | Action |' header"
    )]
    [InlineData(
        "**Key bindings (one meaning each)**\n\n| Key | Action |\n| --- | --- |\n",
        "has no key rows"
    )]
    public void A_deleted_table_fails_instead_of_passing_vacuously(string guide, string expected)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            GuideKeyTable.Parse(guide)
        );

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_is_not_vacuous()
    {
        IReadOnlyList<GuideKeyRow> rows = GuideKeyTable.Parse(ReadGuide());

        Assert.True(
            rows.Sum(row => row.Keys.Count) >= DistinctCatalogKeys().Count,
            "the guide's table documents fewer keys than the catalog has rows"
        );
        Assert.All(rows, row => Assert.NotEmpty(row.Keys));
    }

    [Fact]
    public void The_guide_exists_where_the_check_looks() =>
        Assert.True(File.Exists(Guide), $"docs/guide.md is missing: looked for '{Guide}'");

    private static string ReadGuide() => File.ReadAllText(Guide);

    /// <summary>Catalog keys missing from the table, each with the catalog's own description.</summary>
    private static string[] Gaps(IReadOnlyList<GuideKeyRow> rows)
    {
        var documented = rows.SelectMany(row => row.Keys).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. DistinctCatalogKeys()
                .Where(key => !documented.Contains(key.Spelling))
                .Select(key =>
                    $"KeyBindingCatalog's '{key.Spelling}' ({key.Description}) has no docs/guide.md row"
                ),
        ];
    }

    /// <summary>Table keys with no catalog row, each with the row that documents it.</summary>
    private static string[] Ghosts(IReadOnlyList<GuideKeyRow> rows)
    {
        var catalog = DistinctCatalogKeys()
            .Select(key => key.Spelling)
            .ToHashSet(StringComparer.Ordinal);
        var exempt = Exemptions.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. rows.SelectMany(row => row.Keys.Select(key => (Row: row, Key: key)))
                .Where(entry => !catalog.Contains(entry.Key) && !exempt.Contains(entry.Key))
                .Select(entry =>
                    $"docs/guide.md documents '{entry.Key}' and no catalog row has it (row: '{entry.Row.KeyCell}' — {entry.Row.Action})"
                ),
        ];
    }

    /// <summary>
    /// One entry per distinct key, in the spelling <see cref="KeyPatternFormatter"/> writes, with
    /// the first catalog description that carries it.
    /// </summary>
    private static IReadOnlyList<(string Spelling, string Description)> DistinctCatalogKeys() =>
        [
            .. KeyBindingCatalog
                .All.GroupBy(row => KeyPatternFormatter.Describe(row.Key), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, group.First().Description)),
        ];

    /// <summary>Walks up to <c>lunate.sln</c>, so the check finds the guide from any test host.</summary>
    private static string GuidePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return Path.Combine(directory.FullName, "docs", "guide.md");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"lunate.sln was not found above '{AppContext.BaseDirectory}'; the guide check cannot locate the repository."
        );
    }
}
