using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>/thinking</c> command and its footer segment: the bare form prints the session level and
/// the active model's mapping (read-only, allowed while a turn runs), the set form refuses while a
/// turn runs, sets the level, rebuilds the harness (the next request carries the mapping) and
/// updates the footer, an invalid argument names the allowed values and changes nothing, and the
/// settings file's level shows in the footer from the start.
/// </summary>
public sealed class InteractiveSessionThinkingCommandTests
{
    [Fact]
    public async Task Bare_thinking_prints_the_level_and_the_active_models_mapping()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/thinking");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "thinking: off");

        Assert.Contains(
            "thinking: off — no reasoning support",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Set_updates_the_footer_and_the_next_turn_carries_the_effort()
    {
        using var host = new InteractiveSessionHost(
            environment: PrintModeTestSupport.Environment(model: "effort-model")
        );
        File.WriteAllText(
            host.Temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "effort-model", "provider": "openai", "endpoint": null, "contextWindow": 128000, "supportsTools": true, "reasoning": "effort" }
              ]
            }
            """
        );
        Task run = host.RunAsync();

        host.Console.SendText("/thinking high");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "thinking: high");

        Assert.Contains("think:high", host.LastFrame, StringComparison.Ordinal);

        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal(ReasoningEffort.High, host.Client.RequestOptions[0]!.Reasoning?.Effort);

        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_invalid_argument_is_a_notice_naming_the_allowed_values()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/thinking loud");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "thinking must be");

        Assert.Contains(
            "thinking must be 'off', 'low', 'medium' or 'high'",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("think:", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/thinking");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "thinking: off");

        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_busy_session_refuses_both_thinking_forms_into_the_queue()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        foreach (string command in new[] { "/thinking high", "/thinking" })
        {
            host.Console.SendText(command);
            await host.WaitUntilAsync(() => host.Session.Input.Text == command);
            host.Console.SendEnter();
            await InteractiveSessionCommandTests.WaitForFrameAsync(
                host,
                "slash command cannot be queued"
            );

            Assert.Equal(command, host.Session.Input.Text);
            host.Console.SendCtrlC();
            await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        }

        Assert.DoesNotContain("think:", host.LastFrame, StringComparison.Ordinal);
        Assert.True(host.Session.IsRunning);
        Assert.Single(host.Client.Requests);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_settings_thinking_level_shows_in_the_footer_at_start()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinking": "high"
            }
            """
        );
        Task run = host.RunAsync();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "think:high");

        Assert.Contains("think:high", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
