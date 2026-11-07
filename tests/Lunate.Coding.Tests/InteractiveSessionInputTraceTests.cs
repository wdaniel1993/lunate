using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

[CollectionDefinition("input-trace", DisableParallelization = true)]
public sealed class InputTraceCollection;

/// <summary>
/// The documented diagnostic behind <c>LUNATE_INPUT_TRACE</c>: a reporter can attach the file, so it
/// must hold key kinds, modifiers and routing decisions and never printable text — a credential
/// typed into the masked <c>/login</c> prompt leaves nothing in it but a count.
/// </summary>
[Collection("input-trace")]
public sealed class InteractiveSessionInputTraceTests
{
    private const string Secret = "sk-groq-trace-secret";

    [Fact]
    public async Task A_credential_typed_into_the_secret_prompt_leaves_no_secret_bytes_in_the_trace()
    {
        using var temp = new TempDirectory();
        using var environment = new ProcessEnvironment(
            (Lunate.Tui.InputTrace.HomeVariable, temp.File("home")),
            (Lunate.Tui.InputTrace.Variable, Lunate.Tui.InputTrace.EnabledValue)
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "gpt-4o-mini" },
            chat: new ScriptedChatClient()
        );
        string traceFile = temp.File(Path.Combine("home", "diagnostics", "input-trace.log"));

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The command line itself, and the login flow until the mask is up.
        host.Console.SendText("/login groq");
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "API key for groq");

        // The masked prompt is where the trace could leak: the keys are consumed modally, before
        // the router sees them, and the raw layer sees only what the terminal carried.
        host.Console.SendText(Secret);
        await WaitForFrameAsync(host, "API key for groq: " + new string('*', Secret.Length));
        host.Console.SendEnter();
        await WaitForFrameAsync(host, "logged in: groq");
        await DismissVerificationAsync(host, "groq");
        await QuitAsync(host, run);

        Assert.True(File.Exists(traceFile), "the trace file is written under the state root");
        string trace = File.ReadAllText(traceFile);
        Assert.DoesNotContain(Secret, trace, StringComparison.Ordinal);
        Assert.DoesNotContain("groq-trace", trace, StringComparison.Ordinal);
        // Key kinds are recorded with their routing decision; characters travel as chars=1.
        Assert.Contains("chars=1", trace, StringComparison.Ordinal);
        Assert.Contains("kind=Enter", trace, StringComparison.Ordinal);
        Assert.Contains("routed=", trace, StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(traceFile)
                    & ~(UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit)
            );
        }
    }

    private static async Task DismissVerificationAsync(
        InteractiveSessionHost host,
        string providerId
    )
    {
        string question = $"Verify {providerId} with one request?";
        await WaitForFrameAsync(host, question);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains(question, StringComparison.Ordinal);
        });
    }

    private static Task WaitForFrameAsync(InteractiveSessionHost host, string text) =>
        host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(text, StringComparison.Ordinal);
        });

    private static async Task QuitAsync(InteractiveSessionHost host, Task run)
    {
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
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
