using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The runtime replay test drives the client factory through process environment variables
/// (<c>LUNATE_REPLAY</c>/<c>LUNATE_RECORD</c>), so it runs alone: no other collection may observe
/// those variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeReplayCollection
{
    public const string Name = "runtime-replay";
}

/// <summary>
/// The runtime replay seam end to end: one scripted interactive session replays a committed live
/// recording and renders its turn, with no credential resolving and no provider client anywhere.
/// The fixture is only valid for the session shape it was recorded in (fixed prompt, tool names,
/// model id, input), so any change to that shape needs a re-recording:
/// <c>LUNATE_RECORD=1 [LUNATE_RECORD_PATH=&lt;target&gt;] OPENROUTER_API_KEY=&lt;key&gt;</c> passed to
/// this test; the re-recorded answer may change, update <see cref="RecordedAnswer"/> then.
/// </summary>
[Collection(RuntimeReplayCollection.Name)]
public sealed class RuntimeReplaySessionTests
{
    private const string ModelId = "deepseek/deepseek-v4.1-flash";
    private const string AuthName = "openrouter";
    private const string Prompt = "Reply with exactly: the runtime replay seam works";
    private const string RecordedAnswer = "the runtime replay seam works";

    /// <summary>
    /// A fixed prompt keeps the composed date, working directory and machine facts out of the
    /// request digest; the tools, model id and input are the session's own.
    /// </summary>
    private const string ReplayPrompt = "You are Lunate, a coding agent. Answer the user briefly.";

    [Fact]
    public async Task An_interactive_session_renders_the_turn_of_a_committed_fixture()
    {
        bool recording = RecordingRequested();
        string target = RecordingTarget();
        using var credentials = new TempDirectory();
        AuthStore auth = AuthStore.Load(credentials.File("auth.json"), _ => null);

        if (recording)
        {
            auth.Set(AuthName, RequireLiveKey());
            auth.Save();
        }

        using var host = new InteractiveSessionHost(
            factory: HarnessFactory.CreateFactory(auth),
            environment: SessionEnvironment(),
            configureHarness: options => options with { SystemPrompt = ReplayPrompt }
        );
        WriteModelCatalog(host);
        using var variables = recording
            ? new ProcessEnvironment(
                ("LUNATE_RECORD", "1"),
                ("LUNATE_RECORD_PATH", target),
                ("LUNATE_REPLAY", null)
            )
            : new ProcessEnvironment(
                ("LUNATE_RECORD", null),
                ("LUNATE_RECORD_PATH", null),
                ("LUNATE_REPLAY", FixturePath)
            );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        if (!recording)
        {
            // The replayed model declares this credential and nothing resolves it: serving the
            // fixture at all is the keyless, provider-free proof.
            Assert.Null(auth.TryGet(AuthName));
        }

        host.Console.SendText(Prompt);
        host.Console.SendEnter();
        if (recording)
        {
            // The recorder appends the exchange after the model stream ended, so the written
            // fixture is the record run's completion signal.
            await host.WaitUntilAsync(() => RecordedExchanges(target) > 0, TimeSpan.FromMinutes(5));
        }

        await host.WaitUntilAsync(
            () => host.ScrollbackText.Contains(RecordedAnswer, StringComparison.Ordinal),
            TimeSpan.FromMinutes(1)
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static string FixturePath =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "fixtures",
            "streams",
            "runtime-replay-session.jsonl"
        );

    /// <summary>The factory's own truthiness, so a record run here records where the factory records.</summary>
    private static bool RecordingRequested() =>
        Environment.GetEnvironmentVariable("LUNATE_RECORD") is { } value
        && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>The re-recording target: <c>LUNATE_RECORD_PATH</c> when set, the committed fixture otherwise.</summary>
    private static string RecordingTarget() =>
        Environment.GetEnvironmentVariable("LUNATE_RECORD_PATH") is { Length: > 0 } path
            ? path
            : FixturePath;

    private static string RequireLiveKey() =>
        Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException(
                "Re-recording the runtime replay fixture needs a live OPENROUTER_API_KEY in the "
                    + "environment. The replay run never reads one: unset LUNATE_RECORD to run the "
                    + "committed fixture offline."
            );

    private static int RecordedExchanges(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Length - 1 : 0;

    private static void WriteModelCatalog(InteractiveSessionHost host) =>
        File.WriteAllText(
            host.Temp.File("models.json"),
            $$"""
            {
              "schemaVersion": 1,
              "models": [
                {
                  "id": "{{ModelId}}",
                  "provider": "openai",
                  "endpoint": "https://openrouter.ai/api/v1",
                  "contextWindow": 128000,
                  "supportsTools": true,
                  "auth": "{{AuthName}}"
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
