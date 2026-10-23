namespace Lunate.Tui.Tests;

public sealed class KeyRouterTests
{
    private static readonly (KeyEvent Key, RoutedKey Expected)[] IdleTable =
    [
        (Key(KeyKind.Enter), RoutedKey.Submit),
        (Key(KeyKind.Enter, alt: true), RoutedKey.Edit),
        (Key(KeyKind.Enter, shift: true), RoutedKey.Edit),
        (Key(KeyKind.Character, "j", ctrl: true), RoutedKey.Edit),
        (Key(KeyKind.Escape), RoutedKey.Cancel),
        (Key(KeyKind.Character, "c", ctrl: true), RoutedKey.ClearOrQuit),
        (Key(KeyKind.Character, "d", ctrl: true), RoutedKey.Exit),
        (Key(KeyKind.Character, "l", ctrl: true), RoutedKey.Redraw),
        (Key(KeyKind.Character, "p", alt: true), RoutedKey.ModelPicker),
        (Key(KeyKind.Character, "p", ctrl: true), RoutedKey.Edit),
        (Key(KeyKind.Character, "o", ctrl: true), RoutedKey.TranscriptViewer),
        (Key(KeyKind.Tab), RoutedKey.Complete),
        (Key(KeyKind.Up), RoutedKey.HistoryPrevious),
        (Key(KeyKind.Down), RoutedKey.HistoryNext),
    ];

    private static readonly (KeyEvent Key, RoutedKey Expected)[] BusyTable =
    [
        (Key(KeyKind.Enter), RoutedKey.FollowUp),
        (Key(KeyKind.Enter, alt: true), RoutedKey.Edit),
        (Key(KeyKind.Enter, shift: true), RoutedKey.Edit),
        (Key(KeyKind.Character, "j", ctrl: true), RoutedKey.Edit),
        (Key(KeyKind.Escape), RoutedKey.Cancel),
        (Key(KeyKind.Character, "c", ctrl: true), RoutedKey.Interrupt),
        (Key(KeyKind.Character, "d", ctrl: true), RoutedKey.Exit),
        (Key(KeyKind.Character, "l", ctrl: true), RoutedKey.Redraw),
        (Key(KeyKind.Character, "p", alt: true), RoutedKey.ModelPicker),
        (Key(KeyKind.Character, "p", ctrl: true), RoutedKey.Edit),
        (Key(KeyKind.Character, "o", ctrl: true), RoutedKey.TranscriptViewer),
        (Key(KeyKind.Tab), RoutedKey.Complete),
        (Key(KeyKind.Up), RoutedKey.HistoryPrevious),
        (Key(KeyKind.Down), RoutedKey.HistoryNext),
    ];

    public static TheoryData<KeyEvent, RoutedKey> IdleBindings => Data(IdleTable);

    public static TheoryData<KeyEvent, RoutedKey> BusyBindings => Data(BusyTable);

    [Theory]
    [MemberData(nameof(IdleBindings))]
    public void Every_idle_binding_routes_to_its_own_intent(KeyEvent key, RoutedKey expected) =>
        Assert.Equal(expected, KeyRouter.Route(key));

    [Theory]
    [MemberData(nameof(BusyBindings))]
    public void Every_busy_binding_routes_to_its_own_intent(KeyEvent key, RoutedKey expected) =>
        Assert.Equal(expected, KeyRouter.Route(key, turnRunning: true));

    [Fact]
    public void Enter_submits_idle_and_queues_while_running()
    {
        KeyEvent key = Key(KeyKind.Enter);

        Assert.Equal(RoutedKey.Submit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.FollowUp, KeyRouter.Route(key, turnRunning: true));
    }

    [Theory]
    [MemberData(nameof(NewlineKeys))]
    public void Newline_keys_edit_in_both_states(KeyEvent key)
    {
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key, turnRunning: true));
    }

    public static TheoryData<KeyEvent> NewlineKeys =>
        new()
        {
            Key(KeyKind.Enter, alt: true),
            Key(KeyKind.Enter, shift: true),
            Key(KeyKind.Character, "j", ctrl: true),
        };

    [Fact]
    public void Up_takes_the_queue_back_only_while_entries_wait()
    {
        KeyEvent key = Key(KeyKind.Up);

        Assert.Equal(RoutedKey.HistoryPrevious, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.FollowUpTakeBack, KeyRouter.Route(key, hasQueuedFollowUps: true));
        Assert.Equal(
            RoutedKey.FollowUpTakeBack,
            KeyRouter.Route(key, turnRunning: true, hasQueuedFollowUps: true)
        );
        Assert.Equal(RoutedKey.HistoryPrevious, KeyRouter.Route(key, turnRunning: true));
    }

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
    public void Ctrl_C_interrupts_a_running_turn_and_never_routes_to_cancel()
    {
        KeyEvent key = Key(KeyKind.Character, "c", ctrl: true);

        Assert.Equal(RoutedKey.Interrupt, KeyRouter.Route(key, turnRunning: true));
        Assert.Equal(RoutedKey.ClearOrQuit, KeyRouter.Route(key));
        Assert.NotEqual(RoutedKey.Cancel, KeyRouter.Route(key, turnRunning: true));
    }

    [Fact]
    public void Ctrl_C_does_not_interrupt_when_idle_with_a_queue()
    {
        KeyEvent key = Key(KeyKind.Character, "c", ctrl: true);

        Assert.Equal(RoutedKey.ClearOrQuit, KeyRouter.Route(key, hasQueuedFollowUps: true));
    }

    [Fact]
    public void Ctrl_L_redraws_instead_of_opening_a_picker()
    {
        Assert.Equal(RoutedKey.Redraw, KeyRouter.Route(Key(KeyKind.Character, "l", ctrl: true)));
        Assert.NotEqual(
            RoutedKey.ModelPicker,
            KeyRouter.Route(Key(KeyKind.Character, "l", ctrl: true))
        );
    }

    [Fact]
    public void Alt_P_opens_the_model_picker_and_Ctrl_P_is_unbound()
    {
        Assert.Equal(
            RoutedKey.ModelPicker,
            KeyRouter.Route(Key(KeyKind.Character, "p", alt: true))
        );
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "p", ctrl: true)));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "p")));
    }

    [Fact]
    public void Ctrl_D_exits_in_both_states()
    {
        KeyEvent key = Key(KeyKind.Character, "d", ctrl: true);

        Assert.Equal(RoutedKey.Exit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.Exit, KeyRouter.Route(key, turnRunning: true));
    }

    [Theory]
    [InlineData(KeyKind.F5)]
    [InlineData(KeyKind.PageUp)]
    [InlineData(KeyKind.Unknown)]
    public void Unknown_keys_edit(KeyKind kind) =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(kind)));

    [Fact]
    public void Modified_ctrl_keys_other_than_the_table_edit() =>
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(Key(KeyKind.Character, "y", ctrl: true)));

    private static TheoryData<KeyEvent, RoutedKey> Data((KeyEvent Key, RoutedKey Expected)[] table)
    {
        var data = new TheoryData<KeyEvent, RoutedKey>();
        foreach (var (key, expected) in table)
        {
            data.Add(key, expected);
        }

        return data;
    }

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool shift = false,
        bool alt = false
    ) => new(kind, text, ctrl, shift, alt);
}
