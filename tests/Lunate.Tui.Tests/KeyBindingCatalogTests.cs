namespace Lunate.Tui.Tests;

public sealed class KeyBindingCatalogTests
{
    [Fact]
    public void Every_row_has_a_description()
    {
        Assert.All(
            KeyBindingCatalog.All,
            row => Assert.False(string.IsNullOrWhiteSpace(row.Description), row.Key.ToString())
        );
    }

    [Fact]
    public void Every_row_is_reachable()
    {
        foreach (KeyBinding row in KeyBindingCatalog.All)
        {
            KeyContext context = row.Contexts == KeyContext.None ? KeyContext.Idle : row.Contexts;
            KeyEvent key = KeyOf(row.Key);

            Assert.Equal(row.Intent, KeyRouter.Route(key, context));
        }
    }

    [Fact]
    public void No_two_rows_are_the_same_binding()
    {
        KeyBinding[] rows = [.. KeyBindingCatalog.All];

        for (var i = 0; i < rows.Length; i++)
        {
            for (var j = i + 1; j < rows.Length; j++)
            {
                Assert.False(
                    SameRow(rows[i], rows[j]),
                    $"duplicate binding: '{rows[i].Description}' and '{rows[j].Description}'"
                );
            }
        }
    }

    [Fact]
    public void Earlier_rows_are_never_shadowed_by_later_ones()
    {
        // A row may overlap a later row (that is how specificity works); it may never be shadowed
        // by an earlier one, or it would be dead data that the overlay still advertises.
        KeyBinding[] rows = [.. KeyBindingCatalog.All];

        for (var i = 0; i < rows.Length; i++)
        {
            KeyContext context =
                rows[i].Contexts == KeyContext.None ? KeyContext.Idle : rows[i].Contexts;
            KeyEvent key = KeyOf(rows[i].Key);

            for (var earlier = 0; earlier < i; earlier++)
            {
                bool shadowed =
                    rows[earlier].Key.Matches(key)
                    && rows[earlier].AppliesTo(context)
                    && rows[earlier].Intent != rows[i].Intent;

                Assert.False(
                    shadowed,
                    $"'{rows[i].Description}' is shadowed by '{rows[earlier].Description}'"
                );
            }
        }
    }

    private static KeyEvent KeyOf(KeyPattern pattern) =>
        new(
            pattern.Kind,
            pattern.Text,
            pattern.Ctrl ?? false,
            pattern.Shift ?? false,
            pattern.Alt ?? false
        );

    private static bool SameRow(KeyBinding left, KeyBinding right) =>
        left.Key == right.Key && left.Contexts == right.Contexts;
}
