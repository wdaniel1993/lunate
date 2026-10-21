using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The `@path` file mode end to end: the as-you-type list over the lazy index, acceptance keeping
/// the `@`, the indexing notice and the ignore/laziness guarantees.
/// </summary>
public sealed class PathCompletionTests
{
    private static readonly KeyEvent Tab = new(KeyKind.Tab, null, false, false, false);
    private static readonly KeyEvent Right = new(KeyKind.Right, null, false, false, false);
    private static readonly KeyEvent Left = new(KeyKind.Left, null, false, false, false);

    private static FakeWorkspaceFiles SrcTree() =>
        new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/core")
            .AddFile("src/main.cs");

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

    /// <summary>Nudges the cursor (a no-op at the end) until the lazily built index publishes.</summary>
    private static async Task<AutocompleteState> WaitForFileListAsync(InteractiveSessionHost host)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            host.Console.Send(Right);
            await Task.Delay(10, TestContext.Current.CancellationToken);
            if (host.Session.Autocomplete is { } state)
            {
                return state;
            }
        }

        throw new TimeoutException("the file list never opened");
    }

    [Fact]
    public async Task Typing_an_at_token_lists_and_accepts_a_single_match()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @src/co");
        AutocompleteState state = await WaitForFileListAsync(host);
        Assert.Equal(["src/core/"], state.Items.Select(item => item.Text));

        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/core/");

        host.Console.Complete();
    }

    [Fact]
    public async Task File_completion_matches_names_without_regard_to_case()
    {
        var files = new FakeWorkspaceFiles()
            .AddDirectory("TemplateExtension")
            .AddFile("TempDirectory.cs");
        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "read @te");
        AutocompleteState state = await WaitForFileListAsync(host);
        Assert.Equal(
            ["TempDirectory.cs", "TemplateExtension/"],
            state.Items.Select(item => item.Text)
        );

        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @TempDirectory.cs");

        host.Console.Complete();
    }

    [Fact]
    public async Task Several_matches_let_the_arrows_pick_the_row_to_accept()
    {
        var files = new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/common")
            .AddDirectory("src/compile");
        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "read @src/co");
        AutocompleteState state = await WaitForFileListAsync(host);
        Assert.Equal(["src/common/", "src/compile/"], state.Items.Select(item => item.Text));

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.SelectedIndex == 1);
        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/compile/");

        host.Console.Complete();
    }

    [Fact]
    public async Task Acceptance_preserves_the_text_after_the_cursor()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "read @src/co now");
        host.Console.Send(Left);
        host.Console.Send(Left);
        host.Console.Send(Left);
        host.Console.Send(Left);
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 12);

        // The lazily built index publishes after the cursor settled; each Left/Right pair
        // re-runs the trigger at the same position until the list opens.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (host.Session.Autocomplete is null && DateTime.UtcNow < deadline)
        {
            host.Console.Send(Left);
            host.Console.Send(Right);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(host.Session.Autocomplete);

        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/core/ now");

        host.Console.Complete();
    }

    [Fact]
    public async Task Moving_the_cursor_out_of_the_token_closes_the_list()
    {
        using InteractiveSessionHost host = await StartAsync(SrcTree());

        await SendAsync(host, "@src/co");
        await WaitForFileListAsync(host);
        host.Console.Send(Left);
        await host.WaitUntilAsync(() => host.Session.Autocomplete is null);

        Assert.Equal("@src/co", host.Session.Input.Text);

        host.Console.Complete();
    }

    [Fact]
    public async Task The_index_is_not_built_until_the_first_at_token()
    {
        FakeWorkspaceFiles files = SrcTree();
        using InteractiveSessionHost host = await StartAsync(files);

        await SendAsync(host, "hello world");
        Assert.Equal(0, files.EnumerateCalls);

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        await SendAsync(host, "@al");
        await host.WaitUntilAsync(() => files.EnumerateCalls >= 1);

        host.Console.Complete();
    }

    [Fact]
    public async Task The_session_flow_uses_a_real_tree_and_respects_ignores()
    {
        using var host = new InteractiveSessionHost();
        _ = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, "src", "core"));
        File.WriteAllText(Path.Combine(host.Temp.Root, "src", "main.cs"), string.Empty);
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, "bin"));
        File.WriteAllText(Path.Combine(host.Temp.Root, "bin", "app.dll"), string.Empty);
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, ".git"));
        File.WriteAllText(Path.Combine(host.Temp.Root, ".git", "config"), string.Empty);
        File.WriteAllText(Path.Combine(host.Temp.Root, ".gitignore"), "bin/\n");

        await SendAsync(host, "read @src/c");
        AutocompleteState state = await WaitForFileListAsync(host);
        Assert.Equal(["src/core/"], state.Items.Select(item => item.Text));
        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "read @src/core/");

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "@src/core/");
        await SendAsync(host, "@bin");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Null(host.Session.Autocomplete);

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "@bin");
        await SendAsync(host, "@.git/");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Null(host.Session.Autocomplete);

        host.Console.Complete();
    }
}
