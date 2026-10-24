using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The session-tree surface at the interactive seam: <c>/tree</c> browses the active path and
/// restores a message without sending it, <c>/fork</c> and <c>/clone</c> write their own files with
/// provenance, and every command that writes is refused while a turn is running.
/// </summary>
public sealed class InteractiveSessionTreeTests
{
    [Fact]
    public async Task Tree_shows_the_active_path_and_enter_restores_without_sending()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");

        host.Console.SendText("/tree");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        Assert.Contains("hello", host.Frames, StringComparison.Ordinal);
        Assert.Contains("j/k move", host.Frames, StringComparison.Ordinal);

        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);
        await host.WaitUntilAsync(() => host.Session.Input.Text == "hello");

        Assert.Equal("hello", host.Session.Input.Text);
        Assert.Single(host.Client.Requests);
        Assert.False(host.Session.IsRunning);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Tree_closes_with_escape_and_leaves_the_editor_alone()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");

        host.Console.SendText("/tree");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        Assert.Single(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Fork_writes_a_new_file_with_origin_and_the_prefix()
    {
        using var host = Wide(new InteractiveSessionHost());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));

        await SendLineAsync(host, "/fork e_01");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "forked at e_01");

        string[] files = SessionFiles(host);
        Assert.Equal(2, files.Length);
        string fork = Assert.Single(
            files,
            file => !string.Equals(file, source, StringComparison.Ordinal)
        );
        SessionHeaderEntry header = Session.Load(fork).Header;
        Assert.Equal(new SessionOrigin(Session.Load(source).SessionId, "e_01"), header.Origin);
        Assert.True(header.Branched);
        Assert.Equal(3, header.Schema);
        Assert.Equal(["e_01"], Session.Load(fork).Entries.Select(entry => entry.Id));

        Assert.Contains(source, host.Frames, StringComparison.Ordinal);
        Assert.Contains(fork, host.Frames, StringComparison.Ordinal);
        Assert.Contains("forked at e_01", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Fork_of_an_unknown_entry_reports_it_and_writes_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");

        await SendLineAsync(host, "/fork e_99");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "could not fork");

        Assert.Single(SessionFiles(host));
        Assert.Contains("could not fork", host.Frames, StringComparison.Ordinal);
        Assert.Contains("e_99", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Clone_copies_every_entry_and_names_both_paths()
    {
        using var host = Wide(new InteractiveSessionHost());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));
        Session before = Session.Load(source);

        await SendLineAsync(host, "/clone");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "cloned ");

        string clonePath = Assert.Single(
            SessionFiles(host),
            file => !string.Equals(file, source, StringComparison.Ordinal)
        );
        Session clone = Session.Load(clonePath);
        Assert.Equal(
            new SessionOrigin(before.SessionId, Assert.IsType<string>(before.ActiveLeafId)),
            clone.Header.Origin
        );
        Assert.True(clone.Header.Branched);
        Assert.Equal(
            before.Entries.Select(SessionFormat.Serialize),
            clone.Entries.Select(SessionFormat.Serialize)
        );
        Assert.Contains("cloned " + before.SessionId, host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_running_turn_blocks_fork_clone_and_rewind_but_not_tree()
    {
        using var host = Wide(new InteractiveSessionHost());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));

        // A command can reach the dispatch path while a run is active (an extension source, a
        // binding): the writers must refuse there, not only at the key pump's queue rule.
        host.Client.Gate(2);
        host.Console.SendText("next");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await host.WaitUntilAsync(() => host.Session.IsRunning);
        string[] linesBefore = File.ReadAllLines(source);

        await InvokeAsync(host, "/fork e_01");
        Assert.Contains("a turn is running", host.Frames, StringComparison.Ordinal);
        await InvokeAsync(host, "/clone");
        Assert.Contains("a turn is running", host.Frames, StringComparison.Ordinal);
        await InvokeAsync(host, "/rewind e_01 summary");
        Assert.Contains("a turn is running", host.Frames, StringComparison.Ordinal);

        Assert.Single(SessionFiles(host));
        Assert.Equal(linesBefore, File.ReadAllLines(source));
        Assert.Empty(host.Client.SummaryRequests);

        // Reading is not writing: the tree still opens, and closes on Escape.
        await InvokeAsync(host, "/tree");
        Assert.True(host.Console.InAlternateScreen);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);

        host.Client.Release(2);
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_slash_command_typed_while_running_is_refused_at_the_queue_not_run()
    {
        using var host = Wide(new InteractiveSessionHost());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));

        host.Client.Gate(2);
        host.Console.SendText("next");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await host.WaitUntilAsync(() => host.Session.IsRunning);

        host.Console.SendText("/fork e_01");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "a slash command cannot be queued"
        );

        Assert.Single(SessionFiles(host));

        host.Client.Release(2);
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Summary_rewind_shows_the_estimate_and_one_call_then_writes_on_confirm()
    {
        using var host = Wide(new InteractiveSessionHost());
        host.Client.SummaryUsage = new UsageDetails
        {
            InputTokenCount = 200,
            OutputTokenCount = 30,
        };
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));
        long inputBefore = host.Session.TokenTotals.Input;

        // The prompt states the cost and writes nothing.
        await SendLineAsync(host, "/rewind e_01 summary");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "one model call will be made");

        Assert.Contains("one model call will be made", host.Frames, StringComparison.Ordinal);
        Assert.Contains("input tokens", host.Frames, StringComparison.Ordinal);
        Assert.Empty(host.Client.SummaryRequests);
        Assert.Equal(2, Session.Load(source).Entries.Count);

        await SendLineAsync(host, "/rewind e_01 summary confirm");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "rewound to e_01");

        Assert.Single(host.Client.SummaryRequests);
        Session after = Session.Load(source);
        Assert.IsType<SessionActiveLeafEntry>(after.Entries[^2]);
        SessionBranchSummaryEntry summary = Assert.IsType<SessionBranchSummaryEntry>(
            after.Entries[^1]
        );
        Assert.Equal("e_01", summary.ParentId);
        Assert.Equal("e_02", summary.FromLeafId);
        Assert.Equal("summary of older turns", summary.Summary);
        // The summary is the leaf now: the next append hangs off it, on the destination path.
        Assert.Equal(["e_01", summary.Id], after.ActivePath.Select(entry => entry.Id));
        Assert.Equal(inputBefore + 200, host.Session.TokenTotals.Input);
        Assert.Equal(30, host.Session.TokenTotals.Output);
        Assert.Contains("rewound to e_01", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_rewind_without_the_summary_appends_only_the_leaf_entry()
    {
        using var host = Wide(new InteractiveSessionHost());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");
        string source = Assert.Single(SessionFiles(host));

        await SendLineAsync(host, "/rewind e_01");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "active leaf moved to e_01");

        Session after = Session.Load(source);
        Assert.IsType<SessionActiveLeafEntry>(after.Entries[^1]);
        Assert.Empty(after.Entries.OfType<SessionBranchSummaryEntry>());
        Assert.Empty(host.Client.SummaryRequests);
        Assert.Contains("active leaf moved to e_01", host.Frames, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_help_listing_names_the_tree_commands()
    {
        using var host = new InteractiveSessionHost();
        host.Scrollback.Profile.Width = 400;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await SendLineAsync(host, "/help");

        string scrollback = host.ScrollbackText;
        Assert.Contains("/tree", scrollback, StringComparison.Ordinal);
        Assert.Contains("/fork", scrollback, StringComparison.Ordinal);
        Assert.Contains("/clone", scrollback, StringComparison.Ordinal);
        Assert.Contains("/rewind", scrollback, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static async Task TurnAsync(InteractiveSessionHost host, string input, string answer)
    {
        host.Client.Enqueue(Scripts.Text(answer), Scripts.Stop());
        host.Console.SendText(input);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning && host.Session.Transcript.Count >= 2
        );
    }

    private static async Task SendLineAsync(InteractiveSessionHost host, string line)
    {
        await host.SendLineAsync(line);
        host.Advance(50);
    }

    /// <summary>A console wide enough that a one-line notice is not clipped by the live area.</summary>
    private static InteractiveSessionHost Wide(InteractiveSessionHost host)
    {
        host.Console.Size = new ConsoleSize(200, 24);
        return host;
    }

    /// <summary>The one command dispatch path, called the way a non-typed source would.</summary>
    private static async Task InvokeAsync(InteractiveSessionHost host, string command)
    {
        await host.Session.DispatchCommandAsync(command, TestContext.Current.CancellationToken);
        host.Advance(50);
    }

    private static string[] SessionFiles(InteractiveSessionHost host) =>
        Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl");
}
