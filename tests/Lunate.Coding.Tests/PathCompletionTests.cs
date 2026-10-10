using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The `@path` Tab-completion flow at the session level: token scanning, replacement, candidate
/// notices, the lazy index build behind the indexing notice, and a real temp-tree walk-through.
/// </summary>
public sealed class PathCompletionTests
{
    private static readonly KeyEvent Tab = new(KeyKind.Tab, null, false, false, false);
    private static readonly KeyEvent Left = new(KeyKind.Left, null, false, false, false);

    private static FakeWorkspaceFiles SrcTree() =>
        new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/core")
            .AddFile("src/main.cs");

    [Fact]
    public async Task A_single_match_replaces_the_token_and_keeps_the_at_sign()
    {
        using var host = new InteractiveSessionHost(workspaceFiles: SrcTree());
        Task run = host.RunAsync();

        host.Console.SendText("read @src/co");
        await TabUntilFrameAsync(host, "> read @src/core/");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Several_matches_extend_the_token_to_the_common_prefix()
    {
        var files = new FakeWorkspaceFiles()
            .AddDirectory("src")
            .AddDirectory("src/common")
            .AddDirectory("src/compile");
        using var host = new InteractiveSessionHost(workspaceFiles: files);
        Task run = host.RunAsync();

        host.Console.SendText("read @src/co");
        await TabUntilFrameAsync(host, "> read @src/com");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task No_progress_lists_at_most_twenty_candidates_with_an_ellipsis_count()
    {
        var files = new FakeWorkspaceFiles();
        foreach (char group in "abcde")
        {
            for (var number = 0; number < 5; number++)
            {
                files.AddFile($"{group}{number}");
            }
        }

        using var host = new InteractiveSessionHost(workspaceFiles: files);
        Task run = host.RunAsync();

        host.Console.SendText("@");
        await TabUntilFrameAsync(host, "… (+5 more)");

        Assert.Contains("paths: a0 a1 a2 a3 a4 b0", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("> @", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task No_matches_leaves_the_input_and_says_so()
    {
        using var host = new InteractiveSessionHost(workspaceFiles: SrcTree());
        Task run = host.RunAsync();

        host.Console.SendText("read @nope");
        await TabUntilFrameAsync(host, "paths: no matches");

        Assert.Contains("> read @nope", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_cursor_not_at_the_token_end_falls_through()
    {
        using var host = new InteractiveSessionHost(workspaceFiles: SrcTree());
        Task run = host.RunAsync();

        host.Console.SendText("@src/co");
        host.Console.Send(Left);
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> @src/c!o");

        Assert.DoesNotContain("paths:", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Completion_preserves_the_text_after_the_cursor()
    {
        using var host = new InteractiveSessionHost(workspaceFiles: SrcTree());
        Task run = host.RunAsync();

        host.Console.SendText("read @src/co now");
        host.Console.Send(Left);
        host.Console.Send(Left);
        host.Console.Send(Left);
        host.Console.Send(Left);
        await TabUntilFrameAsync(host, "> read @src/core/ now");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tab_without_an_at_token_does_nothing()
    {
        using var host = new InteractiveSessionHost(workspaceFiles: SrcTree());
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> hello!");

        Assert.DoesNotContain("paths:", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task While_the_index_builds_every_attempt_shows_the_indexing_notice()
    {
        var files = SrcTree();
        files.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new InteractiveSessionHost(workspaceFiles: files);
        Task run = host.RunAsync();

        host.Console.SendText("read @src/co");
        host.Console.Send(Tab);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "file index: indexing…");

        host.Console.Send(Tab);
        await host.WaitUntilAsync(() => files.EnumerateCalls >= 1);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "file index: indexing…");
        Assert.Contains("> read @src/co", host.LastFrame, StringComparison.Ordinal);

        files.Gate.SetResult();
        await TabUntilFrameAsync(host, "> read @src/core/");
        Assert.Equal(1, files.EnumerateCalls);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_index_is_not_built_until_the_first_at_completion()
    {
        var files = SrcTree().AddFile("alpha.txt");
        using var host = new InteractiveSessionHost(workspaceFiles: files);
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.Send(Tab);
        host.Console.SendText("!");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> hello!");
        Assert.Equal(0, files.EnumerateCalls);

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "> hello!");
        host.Console.SendText("@al");
        await TabUntilFrameAsync(host, "> @alpha.txt");
        Assert.Equal(1, files.EnumerateCalls);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_session_flow_completes_against_a_real_tree_and_respects_ignores()
    {
        using var host = new InteractiveSessionHost();
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, "src", "core"));
        File.WriteAllText(Path.Combine(host.Temp.Root, "src", "main.cs"), string.Empty);
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, "bin"));
        File.WriteAllText(Path.Combine(host.Temp.Root, "bin", "app.dll"), string.Empty);
        Directory.CreateDirectory(Path.Combine(host.Temp.Root, ".git"));
        File.WriteAllText(Path.Combine(host.Temp.Root, ".git", "config"), string.Empty);
        File.WriteAllText(Path.Combine(host.Temp.Root, ".gitignore"), "bin/\n");
        Task run = host.RunAsync();

        host.Console.SendText("read @src/c");
        await TabUntilFrameAsync(host, "> read @src/core/");

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "@src/core/");
        host.Console.SendText("@bin");
        await TabUntilFrameAsync(host, "paths: no matches");

        host.Console.SendCtrlC();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "@bin");
        host.Console.SendText("@.git/");
        await TabUntilFrameAsync(host, "paths: no matches");

        host.Console.Complete();
        await run;
    }

    /// <summary>Repeats Tab until the frame shows the expected text (the build is real-async).</summary>
    private static async Task TabUntilFrameAsync(InteractiveSessionHost host, string expected)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow <= deadline)
        {
            host.Console.Send(Tab);
            await Task.Delay(5, TestContext.Current.CancellationToken);
            host.Advance(33);
            if (host.LastFrame.Contains(expected, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new TimeoutException($"the frame never contained '{expected}': {host.LastFrame}");
    }
}
