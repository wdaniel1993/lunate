using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The session's autocomplete state machine: the trigger rules, the filtered rows and the key
/// precedence while the list is open (design D5-D8).
/// </summary>
public sealed class InteractiveSessionAutocompleteTests
{
    private static readonly KeyEvent Down = new(KeyKind.Down, null, false, false, false);
    private static readonly KeyEvent Up = new(KeyKind.Up, null, false, false, false);
    private static readonly KeyEvent Tab = new(KeyKind.Tab, null, false, false, false);
    private static readonly KeyEvent Left = new(KeyKind.Left, null, false, false, false);
    private static readonly KeyEvent AltEnter = new(KeyKind.Enter, null, false, false, true);

    private static async Task<InteractiveSessionHost> StartAsync(
        IWorkspaceFiles? workspaceFiles = null
    )
    {
        var host = new InteractiveSessionHost(workspaceFiles: workspaceFiles);
        _ = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        return host;
    }

    private static async Task SendAsync(InteractiveSessionHost host, string text)
    {
        host.Console.SendText(text);
        await host.WaitUntilAsync(() =>
            host.Session.Input.Text == text && host.Session.Input.CursorPosition == text.Length
        );
    }

    [Fact]
    public async Task Typing_a_slash_opens_the_command_list()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/");

        AutocompleteState state = Assert.IsType<AutocompleteState>(host.Session.Autocomplete);
        Assert.Equal(CompletionMode.Command, state.Mode);
        Assert.Equal(0, state.TokenStart);
        Assert.NotEmpty(state.Items);
        Assert.Equal(AutocompleteState.MaxRows, state.Items.Count);

        host.Console.Complete();
    }

    [Fact]
    public async Task Typing_filters_the_command_list_fuzzily()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/thk");

        AutocompleteState state = Assert.IsType<AutocompleteState>(host.Session.Autocomplete);
        Assert.Equal(["/thinking"], state.Items.Select(item => item.Text));

        host.Console.Complete();
    }

    [Fact]
    public async Task A_query_without_matches_closes_the_list()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/zzz");

        Assert.Null(host.Session.Autocomplete);
        Assert.Equal("/zzz", host.Session.Input.Text);

        host.Console.Complete();
    }

    [Fact]
    public async Task A_space_after_the_command_closes_the_list()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/help ");

        Assert.Null(host.Session.Autocomplete);

        host.Console.Complete();
    }

    [Fact]
    public async Task Moving_the_cursor_back_into_the_command_opens_the_list()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/help x");
        Assert.Null(host.Session.Autocomplete);

        host.Console.Send(new KeyEvent(KeyKind.Home, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 0);
        host.Console.Send(new KeyEvent(KeyKind.Right, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 1);

        AutocompleteState state = Assert.IsType<AutocompleteState>(host.Session.Autocomplete);
        Assert.Equal(CompletionMode.Command, state.Mode);

        host.Console.Complete();
    }

    [Fact]
    public async Task A_newline_closes_command_mode()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/help");
        Assert.NotNull(host.Session.Autocomplete);

        host.Console.Send(AltEnter);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/help\n");

        Assert.Null(host.Session.Autocomplete);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
    }

    [Fact]
    public async Task The_selection_moves_with_the_arrows()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/");
        host.Console.Send(Down);
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.SelectedIndex == 1);
        host.Console.Send(Down);
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.SelectedIndex == 2);
        host.Console.Send(Up);
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.SelectedIndex == 1);

        host.Console.Complete();
    }

    [Fact]
    public async Task The_selection_clamps_at_both_ends()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/thk");
        host.Console.Send(Down);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(0, host.Session.Autocomplete?.SelectedIndex);

        host.Console.Send(Up);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(0, host.Session.Autocomplete?.SelectedIndex);

        host.Console.Complete();
    }

    [Fact]
    public async Task Tab_accepts_the_selected_command_with_a_trailing_space()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/compact ");

        Assert.Null(host.Session.Autocomplete);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
    }

    [Fact]
    public async Task Enter_accepts_the_selected_command_instead_of_submitting()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/compact ");

        Assert.Null(host.Session.Autocomplete);
        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
    }

    [Fact]
    public async Task Enter_submits_once_the_typed_word_is_the_exact_selection()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        Assert.Null(host.Session.Autocomplete);
        Assert.False(host.Session.IsRunning);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
    }

    [Fact]
    public async Task Esc_closes_the_list_and_keeps_the_input()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => host.Session.Autocomplete is null);

        Assert.Equal("/comp", host.Session.Input.Text);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
    }

    [Fact]
    public async Task Esc_does_not_reopen_the_list_on_the_next_cursor_move()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => host.Session.Autocomplete is null);

        host.Console.Send(Left);
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 4);
        Assert.Equal("/comp", host.Session.Input.Text);

        host.Console.Complete();
    }

    [Fact]
    public async Task Typing_after_esc_reopens_the_filtered_list()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => host.Session.Autocomplete is null);

        host.Console.SendText("a");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/compa");

        Assert.NotNull(host.Session.Autocomplete);

        host.Console.Complete();
    }

    [Fact]
    public async Task Tab_without_an_open_list_does_nothing()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "hello");
        host.Console.Send(Tab);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("hello", host.Session.Input.Text);

        host.Console.Complete();
    }

    [Fact]
    public async Task Tab_mid_word_accepts_up_to_the_cursor()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/compact");
        host.Console.Send(Left);
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 7);
        Assert.NotNull(host.Session.Autocomplete);

        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/compact t");

        host.Console.Complete();
    }

    [Fact]
    public async Task Tab_while_a_turn_runs_does_nothing()
    {
        using InteractiveSessionHost host = await StartAsync();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("/comp");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/comp");
        Assert.Null(host.Session.Autocomplete);

        host.Console.Send(Tab);
        host.Console.SendText("!");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/comp!");

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
    }

    [Fact]
    public async Task CtrlC_while_the_list_is_open_clears_the_input_and_closes_it()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        Assert.NotNull(host.Session.Autocomplete);

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        Assert.Null(host.Session.Autocomplete);

        host.Console.Complete();
    }

    [Fact]
    public async Task CtrlL_while_the_list_is_open_still_opens_the_model_picker()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.Send(new KeyEvent(KeyKind.Character, "l", true, false, false));
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select model");

        host.Console.Complete();
    }

    [Fact]
    public async Task CtrlO_while_the_list_is_open_still_opens_the_viewer()
    {
        using InteractiveSessionHost host = await StartAsync();

        await SendAsync(host, "/comp");
        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);

        host.Console.Complete();
    }

    private static FakeWorkspaceFiles SrcTree() =>
        new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/core")
            .AddFile("src/main.cs");

    /// <summary>Nudges the cursor (a no-op at the end) until the lazily built index publishes.</summary>
    private static async Task<AutocompleteState> WaitForFileListAsync(InteractiveSessionHost host)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            host.Console.Send(new KeyEvent(KeyKind.Right, null, false, false, false));
            await Task.Delay(10, TestContext.Current.CancellationToken);
            if (host.Session.Autocomplete is { } state)
            {
                return state;
            }
        }

        throw new TimeoutException("the file list never opened");
    }

    [Fact]
    public async Task Typing_an_at_token_lists_the_matching_files()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @src/co");
        AutocompleteState state = await WaitForFileListAsync(host);

        Assert.Equal(CompletionMode.File, state.Mode);
        Assert.Equal("read ".Length, state.TokenStart);
        Assert.Equal(["src/core/"], state.Items.Select(item => item.Text));
        CompletionItem item = Assert.Single(state.Items);
        Assert.Equal("src/", item.Muted);
        Assert.Equal(CompletionKind.File, item.Kind);

        host.Console.Complete();
    }

    [Fact]
    public async Task Tab_accepts_a_file_row_and_keeps_the_at_sign()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @src/co");
        await WaitForFileListAsync(host);
        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/core/");

        Assert.Null(host.Session.Autocomplete);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
    }

    [Fact]
    public async Task Enter_accepts_a_file_row_instead_of_submitting()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @src/co");
        await WaitForFileListAsync(host);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/core/");

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
    }

    [Fact]
    public async Task The_file_list_is_capped_at_eight_rows()
    {
        var files = new FakeWorkspaceFiles();
        for (var index = 0; index < 20; index++)
        {
            files.AddFile(FormattableString.Invariant($"f{index:00}.txt"));
        }

        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "@f");
        AutocompleteState state = await WaitForFileListAsync(host);

        Assert.Equal(AutocompleteState.MaxRows, state.Items.Count);

        host.Console.Complete();
    }

    [Fact]
    public async Task While_the_index_builds_the_list_stays_closed_and_the_notice_shows()
    {
        FakeWorkspaceFiles files = SrcTree();
        files.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "read @src/co");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("file index: indexing…", StringComparison.Ordinal);
        });
        Assert.Null(host.Session.Autocomplete);
        Assert.Equal(1, files.EnumerateCalls);

        files.Gate.SetResult();
        await WaitForFileListAsync(host);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("file index: indexing…", StringComparison.Ordinal);
        });

        host.Console.Complete();
    }

    [Fact]
    public async Task No_file_matches_keeps_the_list_closed()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @nope");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Null(host.Session.Autocomplete);
        Assert.Equal("read @nope", host.Session.Input.Text);

        host.Console.Complete();
    }

    [Fact]
    public async Task Command_mode_wins_over_file_mode()
    {
        FakeWorkspaceFiles files = SrcTree();
        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "/@src");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Null(host.Session.Autocomplete);
        Assert.Equal(0, files.EnumerateCalls);

        host.Console.Complete();
    }
}
