namespace Lunate.Tui.Tests;

public sealed class KeyRouterTests
{
    private static readonly (KeyEvent Key, RoutedKey Expected)[] Table =
    [
        (Key(KeyKind.Enter), RoutedKey.Submit),
        (Key(KeyKind.Enter, alt: true), RoutedKey.Edit),
        (Key(KeyKind.Character, "j", ctrl: true), RoutedKey.Edit),
        (Key(KeyKind.Escape), RoutedKey.Cancel),
        (Key(KeyKind.Character, "c", ctrl: true), RoutedKey.ClearOrQuit),
        (Key(KeyKind.Character, "l", ctrl: true), RoutedKey.ModelPicker),
        (Key(KeyKind.Character, "p", ctrl: true), RoutedKey.FavoritesCycle),
        (Key(KeyKind.Character, "o", ctrl: true), RoutedKey.TranscriptViewer),
        (Key(KeyKind.Tab), RoutedKey.Complete),
        (Key(KeyKind.Up), RoutedKey.HistoryPrevious),
        (Key(KeyKind.Up, alt: true), RoutedKey.FollowUpPrevious),
        (Key(KeyKind.Down), RoutedKey.HistoryNext),
    ];

    private static readonly (KeyEvent Key, RoutedKey Expected)[] BusyTable =
    [
        (Key(KeyKind.Enter, alt: true), RoutedKey.FollowUp),
    ];

    public static TheoryData<KeyEvent, RoutedKey> Bindings
    {
        get
        {
            var data = new TheoryData<KeyEvent, RoutedKey>();
            foreach (var (key, expected) in Table)
            {
                data.Add(key, expected);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Bindings))]
    public void Every_guide_binding_routes_to_its_own_intent(KeyEvent key, RoutedKey expected) =>
        Assert.Equal(expected, KeyRouter.Route(key));

    [Fact]
    public void Every_binding_has_a_distinct_intent()
    {
        var intents = Table.Select(row => row.Expected).ToArray();

        Assert.Single(intents, intent => intent == RoutedKey.Submit);
        Assert.Single(intents, intent => intent == RoutedKey.Cancel);
        Assert.Single(intents, intent => intent == RoutedKey.ClearOrQuit);
        Assert.Single(intents, intent => intent == RoutedKey.ModelPicker);
        Assert.Single(intents, intent => intent == RoutedKey.FavoritesCycle);
        Assert.Single(intents, intent => intent == RoutedKey.TranscriptViewer);
        Assert.Single(intents, intent => intent == RoutedKey.Complete);
        Assert.Single(intents, intent => intent == RoutedKey.HistoryPrevious);
        Assert.Single(intents, intent => intent == RoutedKey.HistoryNext);
        Assert.Single(intents, intent => intent == RoutedKey.FollowUpPrevious);
        Assert.Single(
            BusyTable.Select(row => row.Expected),
            intent => intent == RoutedKey.FollowUp
        );
        Assert.Equal(2, intents.Count(intent => intent == RoutedKey.Edit));
    }

    [Fact]
    public void Alt_enter_follows_up_only_while_a_turn_runs()
    {
        KeyEvent key = Key(KeyKind.Enter, alt: true);

        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.FollowUp, KeyRouter.Route(key, turnRunning: true));
        Assert.NotEqual(RoutedKey.Submit, KeyRouter.Route(key, turnRunning: true));
    }

    [Fact]
    public void Alt_up_takes_a_follow_up_back_instead_of_navigating_history()
    {
        KeyEvent key = Key(KeyKind.Up, alt: true);

        Assert.Equal(RoutedKey.FollowUpPrevious, KeyRouter.Route(key));
        Assert.NotEqual(RoutedKey.HistoryPrevious, KeyRouter.Route(key));
    }

    [Fact]
    public void Ctrl_j_still_edits_while_a_turn_runs() =>
        Assert.Equal(
            RoutedKey.Edit,
            KeyRouter.Route(Key(KeyKind.Character, "j", ctrl: true), turnRunning: true)
        );

    [Fact]
    public void Tab_completes_and_modified_tab_edits()
    {
        Assert.Equal(RoutedKey.Complete, KeyRouter.Route(Key(KeyKind.Tab)));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Tab, shift: true)));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Tab, alt: true)));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Tab, ctrl: true)));
    }

    [Fact]
    public void Shift_enter_edits_and_never_submits() =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Enter, shift: true)));

    [Fact]
    public void Ctrl_j_edits_before_any_generic_ctrl_handling() =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "j", ctrl: true)));

    [Fact]
    public void Ctrl_P_is_its_own_intent()
    {
        Assert.Equal(
            RoutedKey.FavoritesCycle,
            KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true))
        );
        Assert.NotEqual(
            RoutedKey.ModelPicker,
            KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true))
        );
        Assert.NotEqual(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true)));

        Assert.Equal(
            RoutedKey.Edit,
            KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true, alt: true))
        );
        Assert.Equal(
            RoutedKey.Edit,
            KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true, shift: true))
        );
    }

    [Fact]
    public void Ctrl_c_never_routes_to_cancel() =>
        Assert.NotEqual(RoutedKey.Cancel, KeyRouter.Route(Key(KeyKind.Character, "c", ctrl: true)));

    [Theory]
    [InlineData(KeyKind.F5)]
    [InlineData(KeyKind.PageUp)]
    [InlineData(KeyKind.Unknown)]
    public void Unknown_keys_edit(KeyKind kind) =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(kind)));

    [Fact]
    public void Modified_ctrl_keys_other_than_the_table_edit() =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "y", ctrl: true)));

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool shift = false,
        bool alt = false
    ) => new(kind, text, ctrl, shift, alt);
}
