using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The approval selection end to end: the prompt starts on <c>[n]o</c>, Up/Down move and wrap,
/// Space activates, letters keep deciding over it, the model keeps the selection across a repaint
/// and a resize, <c>?</c> shows the legend, and the type-ahead arm keeps buffered and in-flight
/// keys from answering a question the user has not read.
/// </summary>
public sealed class InteractiveSessionApprovalSelectionTests
{
    [Fact]
    public async Task Down_twice_then_space_approves()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        Assert.Equal(
            ApprovalPromptModel.DefaultSelected,
            approval.Host.Session.PendingApproval!.Selected
        );

        approval.Down();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 0
        );
        approval.Space();
        await approval.Host.Client.WaitForCallAsync(2);

        Assert.Equal("hi", File.ReadAllText(approval.Host.Temp.File("note.txt")));
        await approval.FinishAsync();
    }

    [Fact]
    public async Task Space_on_an_untouched_prompt_denies()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Space();
        await approval.Host.Client.WaitForCallAsync(2);

        Assert.False(File.Exists(approval.Host.Temp.File("note.txt")));
        Assert.Contains("Denied", approval.DenialResult(), StringComparison.Ordinal);
        await approval.FinishAsync();
    }

    [Fact]
    public async Task Up_wraps_to_the_last_choice()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Up();
        approval.Up();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        approval.Space();
        await approval.Host.Client.WaitForCallAsync(2);
        Assert.Equal("hi", File.ReadAllText(approval.Host.Temp.File("note.txt")));
        await approval.FinishAsync();
    }

    [Fact]
    public async Task Enter_denies_even_with_the_selection_on_yes()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Up();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 0
        );

        approval.Enter();
        await approval.Host.Client.WaitForCallAsync(2);

        Assert.False(File.Exists(approval.Host.Temp.File("note.txt")));
        Assert.Contains("Denied", approval.DenialResult(), StringComparison.Ordinal);
        await approval.FinishAsync();
    }

    [Fact]
    public async Task Escape_denies_even_with_the_selection_on_always()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        approval.Escape();
        await approval.Host.Client.WaitForCallAsync(2);

        Assert.False(File.Exists(approval.Host.Temp.File("note.txt")));
        Assert.Contains("Denied", approval.DenialResult(), StringComparison.Ordinal);
        await approval.FinishAsync();
    }

    [Fact]
    public async Task A_letter_decides_over_the_selection()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        approval.Host.Console.SendText("n");
        await approval.Host.Client.WaitForCallAsync(2);

        Assert.False(File.Exists(approval.Host.Temp.File("note.txt")));
        await approval.FinishAsync();
    }

    [Fact]
    public async Task No_other_key_moves_the_selection()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        foreach (
            KeyEvent key in new[]
            {
                new KeyEvent(KeyKind.Left, null, false, false, false),
                new KeyEvent(KeyKind.Right, null, false, false, false),
                new KeyEvent(KeyKind.Tab, null, false, false, false),
                new KeyEvent(KeyKind.PageDown, null, false, false, false),
                new KeyEvent(KeyKind.Character, "k", false, false, false),
            }
        )
        {
            approval.Host.Console.Send(key);
        }

        approval.Host.Advance(33);
        await approval.Host.WaitUntilAsync(() => approval.Host.Session.PendingApproval is not null);
        Assert.Equal(2, approval.Host.Session.PendingApproval!.Selected);

        approval.Host.Console.SendText("n");
        await approval.Host.Client.WaitForCallAsync(2);
        await approval.FinishAsync();
    }

    [Fact]
    public async Task The_selection_survives_a_repaint_and_a_resize()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        approval.Host.Console.PushResize(new ConsoleSize(60, 24));
        await approval.Host.WaitForFrameAsync("Allow write?  [y]es  [n]o  > [a]lways this session");

        Assert.Equal(2, approval.Host.Session.PendingApproval!.Selected);
        Assert.Contains(
            "Allow write?  [y]es  [n]o  > [a]lways this session",
            approval.Host.LastFrame,
            StringComparison.Ordinal
        );

        approval.Host.Console.SendText("n");
        await approval.Host.Client.WaitForCallAsync(2);
        await approval.FinishAsync();
    }

    [Fact]
    public async Task Type_ahead_never_answers_the_prompt()
    {
        using var approval = new Approval();
        await approval.Host.WaitUntilAsync(() => approval.Host.Session.PendingApproval is not null);

        // A key read before the panel rendered never decides.
        await approval.Host.Session.HandleKeyAsync(
            new KeyEvent(KeyKind.Character, "y", false, false, false),
            approval.Host.Session.ApprovalArmSequence,
            TestContext.Current.CancellationToken
        );

        // A key delivered inside the 300 ms window is dropped, not queued.
        approval.Host.Console.SendText("y");
        approval.Host.Advance(299);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(approval.Host.Session.PendingApproval);
        Assert.Single(approval.Host.Client.Requests);
        Assert.False(File.Exists(approval.Host.Temp.File("note.txt")));

        // Past the window, the same key decides.
        approval.Host.Advance(2);
        approval.Host.Console.SendText("y");
        await approval.Host.Client.WaitForCallAsync(2);
        Assert.Equal("hi", File.ReadAllText(approval.Host.Temp.File("note.txt")));
        await approval.FinishAsync();
    }

    [Fact]
    public async Task The_legend_answers_question_mark_without_deciding()
    {
        using var approval = new Approval();
        await approval.Host.WaitForApprovalAsync();
        approval.Down();
        await approval.Host.WaitUntilAsync(() =>
            approval.Host.Session.PendingApproval?.Selected == 2
        );

        approval.Host.Console.SendText("?");
        await approval.Host.WaitForFrameAsync("Space choose");

        Assert.Contains(
            "y approve  n deny  a always for this session",
            approval.Host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Contains("Enter or Escape deny", approval.Host.LastFrame, StringComparison.Ordinal);
        Assert.NotNull(approval.Host.Session.PendingApproval);
        Assert.True(approval.Host.Session.PendingApproval!.ShowLegend);
        Assert.Equal(2, approval.Host.Session.PendingApproval.Selected);

        approval.Host.Console.SendText("n");
        await approval.Host.Client.WaitForCallAsync(2);
        await approval.FinishAsync();
    }

    /// <summary>A session whose first turn asks to write <c>note.txt</c>, already running.</summary>
    private sealed class Approval : IDisposable
    {
        private Task? _run;

        public Approval()
        {
            Host = new InteractiveSessionHost();
            Host.Client.Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            );
            Host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
            _run = Host.RunAsync();
            Host.Console.SendText("go");
            Host.Console.SendEnter();
        }

        public InteractiveSessionHost Host { get; }

        public void Up() => Host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));

        public void Down() =>
            Host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));

        public void Space() =>
            Host.Console.Send(new KeyEvent(KeyKind.Space, null, false, false, false));

        public void Enter() =>
            Host.Console.Send(new KeyEvent(KeyKind.Enter, null, false, false, false));

        public void Escape() =>
            Host.Console.Send(new KeyEvent(KeyKind.Escape, null, false, false, false));

        /// <summary>The model's answer after a denial, as the harness passed it back.</summary>
        public string DenialResult() =>
            Assert
                .Single(Host.Client.Requests[1][^1].Contents.OfType<FunctionResultContent>())
                .Result!.ToString()!;

        public async Task FinishAsync()
        {
            Host.Console.Complete();
            if (_run is { } run)
            {
                await run;
                _run = null;
            }
        }

        public void Dispose() => Host.Dispose();
    }
}
