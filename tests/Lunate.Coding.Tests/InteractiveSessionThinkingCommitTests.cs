using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The commit half of the interactive pump's thinking display: a parent run's <c>ReasoningEnd</c>
/// commits the collapsed <c>✦ thought for &lt;n&gt;s</c> line (whole seconds on the injected
/// scheduler) and clears the live block, the <c>thinkingFull</c> setting commits the full text
/// instead, and a child run's reasoning commits nothing.
/// </summary>
public sealed class InteractiveSessionThinkingCommitTests
{
    [Fact]
    public async Task The_reasoning_end_commits_the_collapsed_line_with_the_elapsed_seconds()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new ReasoningStart("run_parent", "msg_r"));
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_parent", "msg_r", "weighing the options")
        );
        host.Advance(12_000);
        host.Session.HandleEvent(new ReasoningEnd("run_parent", "msg_r"));
        host.Advance(40);

        Assert.Contains("✦ thought for 12s\n", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "weighing the options",
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("thinking…", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_thinking_full_setting_commits_the_wrapped_indented_text()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinkingFull": true
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new ReasoningStart("run_parent", "msg_r"));
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_parent", "msg_r", new string('a', 90))
        );
        host.Session.HandleEvent(new ReasoningEnd("run_parent", "msg_r"));
        host.Advance(40);

        Assert.Contains(
            "  " + new string('a', 78) + "\n  " + new string('a', 12) + "\n",
            host.ScrollbackText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("thought for", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_child_runs_reasoning_commits_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run_child") { ParentRunId = "run_parent" });
        host.Session.HandleEvent(
            new ReasoningStart("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_child", "msg_c", "secret child reasoning")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Advance(12_000);
        host.Session.HandleEvent(
            new ReasoningEnd("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Advance(40);

        Assert.DoesNotContain("thought for", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret child reasoning",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }
}
