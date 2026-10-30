using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The footer's turn-cost and favourite wiring: a priced turn shows its exact cost, a second turn
/// starts fresh, an unpriced model leaves the cost segment out, the position comes from the
/// settings list, and <c>/favorite</c> updates the footer immediately.
/// </summary>
public sealed class InteractiveSessionFooterCostTests
{
    private const string Models = """
        {
          "schemaVersion": 2,
          "models": [
            {
              "id": "fixture-priced",
              "provider": "openai",
              "contextWindow": 128000,
              "supportsTools": true,
              "pricing": { "input": 2.5, "output": 10, "cacheRead": 1.25, "cacheWrite": 3.75 }
            },
            {
              "id": "fixture-unpriced",
              "provider": "openai",
              "contextWindow": 128000,
              "supportsTools": true
            }
          ]
        }
        """;

    [Fact]
    public async Task A_priced_turn_shows_its_cost_in_the_footer()
    {
        using var host = Host("fixture-priced");
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        // Capture the frame the wait accepted: LastFrame is a moving surface, and under load the
        // next paint can replace it between the wait returning and the assertion reading it again.
        string frame = string.Empty;
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            frame = host.LastFrame;
            return frame.Contains("$0.0075", StringComparison.Ordinal);
        });

        Assert.Contains("1.5k/128.0k", frame, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_second_turn_starts_with_a_fresh_cost()
    {
        using var host = Host("fixture-priced");
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Usage(2000, 100), Scripts.Text("second"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.Session.IsRunning
                && host.LastFrame.Contains("$0.0075", StringComparison.Ordinal);
        });

        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("$0.0060", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("$0.0075", host.LastFrame, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_new_turn_without_usage_clears_the_previous_cost()
    {
        using var host = Host("fixture-priced");
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.Session.IsRunning
                && host.LastFrame.Contains("$0.0075", StringComparison.Ordinal);
        });

        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.Session.IsRunning
                && !host.LastFrame.Contains("$", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_priced_zero_token_turn_shows_zero_cost()
    {
        using var host = Host("fixture-priced");
        host.Client.Enqueue(Usage(0, 0), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.Session.IsRunning
                && host.LastFrame.Contains("$0.00", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("$0.0000", host.LastFrame, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_unpriced_turn_omits_the_cost_segment()
    {
        using var host = Host("fixture-unpriced");
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("1.5k/128.0k", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("$", host.LastFrame, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_footer_shows_the_active_models_favourite_position()
    {
        using var host = Host("fixture-priced");
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "favorites": ["fixture-unpriced", "fixture-priced"]
            }
            """
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("fav:2/2", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Adding_a_favourite_updates_the_footer_position_immediately()
    {
        using var host = Host("fixture-priced");
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("fixture-priced", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("fav:", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/favorite");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("fav:1/1", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    private static InteractiveSessionHost Host(string model)
    {
        var host = new InteractiveSessionHost(launch: new LaunchOptions { Model = model });
        File.WriteAllText(host.Temp.File("models.json"), Models);
        return host;
    }

    private static ChatResponseUpdate Usage(long input, long output) =>
        new(
            ChatRole.Assistant,
            [
                new UsageContent(
                    new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
                ),
            ]
        );
}
