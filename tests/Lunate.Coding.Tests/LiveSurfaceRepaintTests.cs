using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The narrow live surfaces (spec: the terminal contract; T-33 width bands): the approval prompt,
/// the model picker and the question tool each render and repaint at 40 columns without leaving a
/// duplicate or leftover row. Each drives the real host/console, lets the live area repaint (the
/// frame and spinner ticks), then reads the resulting physical rows through the terminal-screen
/// model and checks every key line appears exactly once.
/// </summary>
public sealed class LiveSurfaceRepaintTests
{
    [Fact]
    public async Task The_approval_prompt_repaints_without_leftover_rows_at_40_columns()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(40, 24);
        host.Client.Enqueue(
            Scripts.Call("call-1", "write", Scripts.Args(("path", "note.txt"), ("content", "hi"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        host.Advance(300);

        IReadOnlyList<string> lines = Screen(host);
        AssertLineOnce(lines, "Allow write?");

        host.Console.SendText("n");
        await host.Client.WaitForCallAsync(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_model_picker_repaints_without_leftover_rows_at_40_columns()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(40, 24);
        SettingsStore.WriteFavorites(["gpt-4o-mini", "gpt-4o"], host.Temp.File("settings.json"));
        Task run = host.RunAsync();

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "p", Ctrl: false, Shift: false, Alt: true)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        host.Advance(300);

        IReadOnlyList<string> lines = Screen(host);
        AssertLineOnce(lines, "gpt-4o-mini (openai)");
        AssertLineOnce(lines, "gpt-4o (openai)");

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select model");
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_question_tool_repaints_without_leftover_rows_at_40_columns()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(40, 24);
        host.Client.Enqueue(
            Scripts.Call(
                "question-call",
                "ask_user",
                Scripts.Args(
                    (
                        "questions",
                        new[]
                        {
                            new
                            {
                                header = "Plan",
                                question = "Which plan should I use?",
                                options = new[]
                                {
                                    new { label = "Simple", description = "Fewer moving parts." },
                                    new { label = "Flexible", description = "More room to grow." },
                                },
                                multiSelect = false,
                            },
                        }
                    )
                )
            ),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("I'll use your choice."), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitForFrameAsync("Which plan should I use?");
        host.Advance(300);

        IReadOnlyList<string> lines = Screen(host);
        AssertLineOnce(lines, "Which plan should I use?");
        AssertLineOnce(lines, "Simple");
        AssertLineOnce(lines, "Flexible");

        // The repaint advance above lands exactly on the picker's 300 ms in-flight window; the wait
        // ages the arm past it so the answer is a key that decides, not one the window drops.
        await host.WaitForQuestionAsync();
        host.Console.SendText("2");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        host.Console.Complete();
        await run;
    }

    private static IReadOnlyList<string> Screen(InteractiveSessionHost host)
    {
        var screen = new TerminalScreen(40);
        foreach (string write in host.Console.Writes)
        {
            screen.Feed(write);
        }

        return screen.Lines;
    }

    private static void AssertLineOnce(IReadOnlyList<string> lines, string marker)
    {
        Assert.Equal(1, lines.Count(line => line.Contains(marker, StringComparison.Ordinal)));
    }
}
