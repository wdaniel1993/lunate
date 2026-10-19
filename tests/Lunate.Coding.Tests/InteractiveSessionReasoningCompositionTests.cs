using System.Text.RegularExpressions;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for thinking ("spec'd but wired"): the real host replays the
/// committed scripted reasoning fixture through the real client factory and renders it — the live
/// thinking block while the span streams, exactly one collapsed line after the end, and the full
/// text when <c>thinkingFull</c> is on. The run's own event stream is validator-clean.
/// </summary>
[Collection(RuntimeReplayCollection.Name)]
public sealed class InteractiveSessionReasoningCompositionTests
{
    private const string ModelId = "synthetic";
    private const string ReplayPrompt = "You are Lunate, a coding agent. Answer the user briefly.";
    private const string Prompt = "What is the answer?";
    private const string ReasoningText = "Let me weigh the options.";
    private const string Answer = "The answer is 42.";

    [Fact]
    public async Task The_replayed_fixture_renders_the_live_thinking_block_then_one_collapsed_line()
    {
        var gate = new ReasoningStreamGate();
        using var host = CreateHost(hooks: gate);
        using var variables = ReplayEnvironment();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText(Prompt);
        host.Console.SendEnter();

        // The harness awaits the provider-stream hook per update, so the stream parks inside the
        // reasoning span: the live frame is asserted while the block is deterministically live.
        await gate.Reached.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("⠹ thinking…", StringComparison.Ordinal)
                && host.LastFrame.Contains("Let me weigh", StringComparison.Ordinal);
        });
        gate.Release();

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.ScrollbackText.Contains("✦ thought for", StringComparison.Ordinal)
                && host.ScrollbackText.Contains(Answer, StringComparison.Ordinal);
        });

        // After ReasoningEnd: exactly one collapsed line, and the live block is gone.
        string scrollback = host.ScrollbackText;
        Assert.Single(Regex.Matches(scrollback, "✦ thought for \\d+s"));
        Assert.DoesNotContain(ReasoningText, scrollback, StringComparison.Ordinal);
        host.Advance(40);
        Assert.DoesNotContain("thinking…", host.LastFrame, StringComparison.Ordinal);

        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        Assert.Empty(EventSequenceValidator.Validate(host.Events));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Thinking_full_commits_the_wrapped_reasoning_text()
    {
        using var host = CreateHost(
            """
            {
              "schemaVersion": 1,
              "thinkingFull": true
            }
            """
        );
        using var variables = ReplayEnvironment();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText(Prompt);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.ScrollbackText.Contains(ReasoningText, StringComparison.Ordinal);
        });

        Assert.Contains("  " + ReasoningText, host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("thought for", host.ScrollbackText, StringComparison.Ordinal);

        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static InteractiveSessionHost CreateHost(
        string? settingsJson = null,
        IAgentHookPoints? hooks = null
    )
    {
        AuthStore auth = AuthStore.Load(
            Path.Combine(Path.GetTempPath(), "lunate-reasoning-" + Guid.NewGuid().ToString("N")),
            _ => null
        );
        var host = new InteractiveSessionHost(
            factory: HarnessFactory.CreateFactory(auth),
            environment: SessionEnvironment(),
            configureHarness: options =>
                options with
                {
                    SystemPrompt = ReplayPrompt,
                    Hooks = hooks ?? options.Hooks,
                }
        );
        WriteModelCatalog(host);
        if (settingsJson is not null)
        {
            File.WriteAllText(host.Temp.File("settings.json"), settingsJson);
        }

        return host;
    }

    /// <summary>Wires the committed scripted reasoning fixture through the replay seam.</summary>
    private static ProcessEnvironment ReplayEnvironment() =>
        new(("LUNATE_RECORD", null), ("LUNATE_RECORD_PATH", null), ("LUNATE_REPLAY", FixturePath));

    private static string FixturePath =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "fixtures",
            "streams",
            "scripted-reasoning.jsonl"
        );

    private static void WriteModelCatalog(InteractiveSessionHost host) =>
        File.WriteAllText(
            host.Temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                {
                  "id": "synthetic",
                  "provider": "openai",
                  "endpoint": "https://openrouter.ai/api/v1",
                  "contextWindow": 128000,
                  "supportsTools": true
                }
              ]
            }
            """
        );

    private static Func<string, string?> SessionEnvironment()
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name => name == "LUNATE_MODEL" ? ModelId : baseline(name);
    }

    /// <summary>
    /// Pauses the replayed stream inside the reasoning span: the harness awaits the provider-stream
    /// hook for every update, so the live thinking block stays painted until the test releases it.
    /// </summary>
    private sealed class ReasoningStreamGate : IAgentHookPoints
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>Completes once the stream reached the second reasoning fragment.</summary>
        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProviderStreamEventAsync(
            string runId,
            string? parentRunId,
            ChatResponseUpdate update,
            CancellationToken cancellationToken
        )
        {
            if (
                update
                    .Contents.OfType<TextReasoningContent>()
                    .Any(content => content.Text == "the options.")
            )
            {
                Reached.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => _release.TrySetResult();
    }

    /// <summary>Sets process environment variables for the scope and restores the previous values.</summary>
    private sealed class ProcessEnvironment : IDisposable
    {
        private readonly (string Name, string? Value)[] _previous;

        public ProcessEnvironment(params (string Name, string? Value)[] variables)
        {
            _previous =
            [
                .. variables.Select(variable =>
                    (variable.Name, Environment.GetEnvironmentVariable(variable.Name))
                ),
            ];
            foreach ((string name, string? value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
