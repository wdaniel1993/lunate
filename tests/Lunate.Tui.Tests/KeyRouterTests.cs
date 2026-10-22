namespace Lunate.Tui.Tests;

public sealed class KeyRouterTests
{
    private static readonly (KeyEvent Key, RoutedKey Expected)[] IdleTable =
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

    private static readonly (KeyEvent Key, RoutedKey Expected)[] BusyQueueTable =
    [
        (Key(KeyKind.Enter), RoutedKey.FollowUp),
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

    private static readonly (KeyEvent Key, RoutedKey Expected)[] BusySteerTable =
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

    public static TheoryData<KeyEvent, RoutedKey> IdleBindings => Data(IdleTable);

    public static TheoryData<KeyEvent, RoutedKey> BusyQueueBindings => Data(BusyQueueTable);

    public static TheoryData<KeyEvent, RoutedKey> BusySteerBindings => Data(BusySteerTable);

    [Theory]
    [MemberData(nameof(IdleBindings))]
    public void Every_idle_binding_routes_to_its_own_intent(KeyEvent key, RoutedKey expected) =>
        Assert.Equal(expected, KeyRouter.Route(key));

    [Theory]
    [MemberData(nameof(BusyQueueBindings))]
    public void Every_busy_binding_routes_to_its_own_intent_under_the_default(
        KeyEvent key,
        RoutedKey expected
    ) => Assert.Equal(expected, KeyRouter.Route(key, turnRunning: true));

    [Theory]
    [MemberData(nameof(BusySteerBindings))]
    public void Every_busy_binding_routes_to_its_own_intent_under_steer(
        KeyEvent key,
        RoutedKey expected
    ) =>
        Assert.Equal(
            expected,
            KeyRouter.Route(key, turnRunning: true, busyEnter: BusyEnterMode.Steer)
        );

    [Fact]
    public void Every_binding_has_a_distinct_intent()
    {
        AssertDistinct(IdleTable, submits: 1, followUps: 0);
    }

    [Fact]
    public void The_busy_default_matrix_has_one_follow_up_key()
    {
        RoutedKey[] intents = AssertDistinct(BusyQueueTable, submits: 0, followUps: 1);

        Assert.Single(intents, intent => intent == RoutedKey.FollowUp);
    }

    [Fact]
    public void The_busy_steer_matrix_has_no_follow_up_key()
    {
        RoutedKey[] intents = AssertDistinct(BusySteerTable, submits: 1, followUps: 0);

        Assert.DoesNotContain(RoutedKey.FollowUp, intents);
    }

    [Fact]
    public void Enter_submits_idle_queues_by_default_and_steers_under_steer()
    {
        KeyEvent key = Key(KeyKind.Enter);

        Assert.Equal(RoutedKey.Submit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.FollowUp, KeyRouter.Route(key, turnRunning: true));
        Assert.Equal(
            RoutedKey.Submit,
            KeyRouter.Route(key, turnRunning: true, busyEnter: BusyEnterMode.Steer)
        );
    }

    [Fact]
    public void Alt_enter_edits_in_every_state_and_setting()
    {
        KeyEvent key = Key(KeyKind.Enter, alt: true);

        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key, turnRunning: true));
        Assert.Equal(
            RoutedKey.Edit,
            KeyRouter.Route(key, turnRunning: true, busyEnter: BusyEnterMode.Steer)
        );
    }

    [Fact]
    public void Ctrl_j_edits_in_every_state_and_setting()
    {
        KeyEvent key = Key(KeyKind.Character, "j", ctrl: true);

        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key));
        Assert.Equal(RoutedKey.Edit, KeyRouter.Route(key, turnRunning: true));
        Assert.Equal(
            RoutedKey.Edit,
            KeyRouter.Route(key, turnRunning: true, busyEnter: BusyEnterMode.Steer)
        );
    }

    [Fact]
    public void Alt_up_takes_a_follow_up_back_instead_of_navigating_history()
    {
        KeyEvent key = Key(KeyKind.Up, alt: true);

        Assert.Equal(RoutedKey.FollowUpPrevious, KeyRouter.Route(key));
        Assert.NotEqual(RoutedKey.HistoryPrevious, KeyRouter.Route(key));
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

    private static TheoryData<KeyEvent, RoutedKey> Data(
        (KeyEvent Key, RoutedKey Expected)[] table
    )
    {
        var data = new TheoryData<KeyEvent, RoutedKey>();
        foreach (var (key, expected) in table)
        {
            data.Add(key, expected);
        }

        return data;
    }

    private static RoutedKey[] AssertDistinct(
        (KeyEvent Key, RoutedKey Expected)[] table,
        int submits,
        int followUps
    )
    {
        RoutedKey[] intents = [.. table.Select(row => row.Expected)];

        Assert.Equal(submits, intents.Count(intent => intent == RoutedKey.Submit));
        Assert.Equal(followUps, intents.Count(intent => intent == RoutedKey.FollowUp));
        Assert.Single(intents, intent => intent == RoutedKey.Cancel);
        Assert.Single(intents, intent => intent == RoutedKey.ClearOrQuit);
        Assert.Single(intents, intent => intent == RoutedKey.ModelPicker);
        Assert.Single(intents, intent => intent == RoutedKey.FavoritesCycle);
        Assert.Single(intents, intent => intent == RoutedKey.TranscriptViewer);
        Assert.Single(intents, intent => intent == RoutedKey.Complete);
        Assert.Single(intents, intent => intent == RoutedKey.HistoryPrevious);
        Assert.Single(intents, intent => intent == RoutedKey.HistoryNext);
        Assert.Single(intents, intent => intent == RoutedKey.FollowUpPrevious);
        Assert.Equal(2, intents.Count(intent => intent == RoutedKey.Edit));
        return intents;
    }

    private static KeyEvent Key(
        KeyKind kind,
        string? text = null,
        bool ctrl = false,
        bool shift = false,
        bool alt = false
    ) => new(kind, text, ctrl, shift, alt);
}
