namespace Lunate.Tui.Tests;

/// <summary>
/// Remapping a binding by id: the catalogue keeps its one row per id — same intent, context,
/// description and position — and only the key changes. The router routes through the real
/// catalogue, so a remap is visible to both the help overlay and the key table's source of truth.
/// Every test restores the catalogue's shipped keys afterwards; the catalogue is static.
/// </summary>
public sealed class KeyBindingCatalogRemapTests
{
    private static (string Id, KeyPattern[] Keys)[] Before() => [.. KeyBindingCatalog.Snapshot()];

    private static void Restore((string Id, KeyPattern[] Keys)[] snapshot)
    {
        foreach ((string id, KeyPattern[] keys) in snapshot)
        {
            KeyBindingCatalog.Remap(id, keys);
        }
    }

    [Fact]
    public void Remap_moves_one_binding_and_keeps_its_row_description_and_intent()
    {
        (string Id, KeyPattern[] Keys)[] before = Before();
        try
        {
            IReadOnlyList<KeyBinding> rowsOfId = KeyBindingCatalog.BindingsFor("ctrl-d.quit");
            string description = rowsOfId[0].Description;
            RoutedKey intent = rowsOfId[0].Intent;
            KeyContext contexts = rowsOfId[0].Contexts;

            Assert.Null(KeyBindingCatalog.Remap("ctrl-d.quit", Keys("Ctrl+T")));

            IReadOnlyList<KeyBinding> after = KeyBindingCatalog.BindingsFor("ctrl-d.quit");
            KeyBinding row = Assert.Single(after);
            Assert.Equal(description, row.Description);
            Assert.Equal(intent, row.Intent);
            Assert.Equal(contexts, row.Contexts);
            Assert.Equal(KeyKind.Character, row.Key.Kind);
            Assert.Equal("t", row.Key.Text);
            Assert.True(row.Key.Ctrl);
            // The shipped key no longer routes: the remap replaces the binding's keys.
            Assert.NotEqual(
                RoutedKey.Exit,
                KeyRouter.Route(Key(KeyKind.Character, "d", ctrl: true))
            );
        }
        finally
        {
            Restore(before);
        }
    }

    [Fact]
    public void A_remapped_action_is_routed_by_the_new_key()
    {
        (string Id, KeyPattern[] Keys)[] before = Before();
        try
        {
            Assert.Null(KeyBindingCatalog.Remap("ctrl-d.quit", Keys("Ctrl+Y")));

            Assert.Equal(RoutedKey.Exit, KeyRouter.Route(Key(KeyKind.Character, "y", ctrl: true)));
        }
        finally
        {
            Restore(before);
        }
    }

    [Fact]
    public void An_unknown_id_is_refused_with_the_nearest_catalogue_ids()
    {
        string? problem = KeyBindingCatalog.Remap("enter.submit.iddle", Keys("Enter"));
        Assert.False(string.IsNullOrWhiteSpace(problem));
        Assert.Contains("enter.submit.idle", problem, StringComparison.Ordinal);
        Assert.Contains("enter.submit.bash", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_an_earlier_binding_owns_is_refused_not_silently_shadowed()
    {
        (string Id, KeyPattern[] Keys)[] before = Before();
        try
        {
            // newline.ctrl-j (an earlier row) already owns Ctrl+J in the idle context, so
            // moving enter.submit.idle there would leave it dead.
            string? problem = KeyBindingCatalog.Remap("enter.submit.idle", Keys("Ctrl+J"));
            Assert.False(string.IsNullOrWhiteSpace(problem));
            Assert.Contains("newline.ctrl-j", problem, StringComparison.Ordinal);
            Assert.Equal(RoutedKey.Submit, KeyRouter.Route(Key(KeyKind.Enter), turnRunning: false));
        }
        finally
        {
            Restore(before);
        }
    }

    [Fact]
    public void Remapping_restores_its_own_rows_back()
    {
        (string Id, KeyPattern[] Keys)[] before = Before();
        _ = KeyBindingCatalog.Remap("ctrl-d.quit", Keys("Ctrl+Y"));
        var undo = (
            Id: "ctrl-d.quit",
            Keys: new[] { before.First(entry => entry.Id == "ctrl-d.quit").Keys[0] }
        );

        Assert.Null(KeyBindingCatalog.Remap(undo.Id, undo.Keys));
        IReadOnlyList<KeyBinding> rows = KeyBindingCatalog.BindingsFor("ctrl-d.quit");
        Assert.Equal(KeyKind.Character, Assert.Single(rows).Key.Kind);
        Assert.Equal("d", Assert.Single(rows).Key.Text);
    }

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool alt = false
    ) => new(kind, text, ctrl, Shift: false, alt);

    private static KeyPattern[] Keys(string spec)
    {
        Assert.True(KeyString.TryParse(spec, out KeyPattern pattern, out _));
        return [pattern];
    }
}
