using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The launch flags resolved over the settings in the interactive session: <c>--model</c> beats the
/// environment and file value, <c>--thinking</c> beats the settings file, and an unknown model
/// keeps the catalog's actionable message.
/// </summary>
public sealed class InteractiveSessionLaunchOptionsTests
{
    [Fact]
    public async Task A_launch_model_wins_over_the_resolved_settings()
    {
        // The settings file names one model and LUNATE_MODEL another; the launch flag wins over both.
        var factory = new FakeChatClientFactory(new ScriptedChatClient());
        using var host = new InteractiveSessionHost(
            factory: factory,
            environment: Environment(("OPENAI_API_KEY", "test-key")),
            launch: new LaunchOptions { Model = "gpt-4o" }
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{ "schemaVersion": 1, "model": "claude-sonnet-5-5" }"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "gpt-4o · ");

        Assert.Equal("gpt-4o", factory.CreatedModel!.Id);
        Assert.Contains("gpt-4o · 128,000 ctx", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("gpt-4o · 0/128.0k", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_launch_thinking_level_turns_thinking_on_over_the_default()
    {
        using var host = new InteractiveSessionHost(
            environment: PrintModeTestSupport.Environment(model: "effort-model"),
            launch: new LaunchOptions { Thinking = ThinkingLevel.High }
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
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

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
    public async Task A_launch_thinking_level_wins_over_the_settings_file()
    {
        using var host = new InteractiveSessionHost(
            environment: PrintModeTestSupport.Environment(model: "effort-model"),
            launch: new LaunchOptions { Thinking = ThinkingLevel.High }
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{ "schemaVersion": 1, "thinking": "low" }"""
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
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "think:high");

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
    public async Task A_launch_model_not_in_the_catalog_is_a_usage_error()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "nope-9000" }
        );
        host.Console.Complete();

        await host.RunAsync();

        Assert.Equal(2, host.Session.ExitCode);
        Assert.Contains(
            "Model 'nope-9000' is not in the catalog",
            host.Diagnostics,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_settings_model_not_in_the_catalog_keeps_the_run_error_exit()
    {
        using var host = new InteractiveSessionHost(
            environment: PrintModeTestSupport.Environment(model: "nope-9000")
        );
        host.Console.Complete();

        await host.RunAsync();

        Assert.Equal(1, host.Session.ExitCode);
        Assert.Contains(
            "Model 'nope-9000' is not in the catalog",
            host.Diagnostics,
            StringComparison.Ordinal
        );
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] overrides)
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name =>
        {
            foreach ((string overridden, string value) in overrides)
            {
                if (string.Equals(name, overridden, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return baseline(name);
        };
    }
}
