using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension.Tests;

/// <summary>
/// The result composition on its own — no child process. A run that wrote nothing, one that died
/// before the handshake and one that stalled after it must be told apart in the text the model (and
/// a CI log) reads, with the exit code and stderr that back the claim.
/// </summary>
public sealed class CodeModeOutcomeTests
{
    private static readonly CodeModeSettings Settings = new("node", 30, 65_536, 100);

    [Fact]
    public void A_silent_timeout_reports_the_runtime_the_elapsed_time_and_the_missing_handshake()
    {
        ProcessRunResult run = new(null, string.Empty, "node: warning", TimedOut: true);

        ToolResult result = CodeModeOutcome.Compose(
            Settings,
            Facts(logs: string.Empty, helloSeen: false),
            run,
            timeoutSeconds: 30,
            elapsed: TimeSpan.FromSeconds(30.4)
        );

        Assert.True(result.IsError);
        // The empty run is no longer folded into the script-timeout text: it says what was missing.
        Assert.DoesNotContain("The script timed out", result.Output, StringComparison.Ordinal);
        Assert.Contains("'node'", result.Output, StringComparison.Ordinal);
        Assert.Contains("wrote nothing in 30.4s", result.Output, StringComparison.Ordinal);
        Assert.Contains("no hello handshake", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "machine was too slow to start it",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("stderr:\nnode: warning", result.Output, StringComparison.Ordinal);

        JsonElement structured = Structured(result);
        Assert.True(structured.GetProperty("timedOut").GetBoolean());
        Assert.False(structured.GetProperty("helloSeen").GetBoolean());
        Assert.Equal(30_400, structured.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(string.Empty, structured.GetProperty("output").GetString());
        Assert.Equal("node: warning", structured.GetProperty("stderr").GetString());
    }

    [Fact]
    public void A_runtime_that_exits_before_the_handshake_reports_its_exit_code_and_stderr()
    {
        ProcessRunResult run = new(
            1,
            string.Empty,
            "Error: Cannot find module 'readline'",
            TimedOut: false
        );

        ToolResult result = CodeModeOutcome.Compose(
            Settings,
            Facts(),
            run,
            timeoutSeconds: 30,
            elapsed: TimeSpan.FromSeconds(0.2)
        );

        Assert.True(result.IsError);
        Assert.Contains("protocol", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "exited with code 1 without writing anything on stdout",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("exit code: 1", result.Output, StringComparison.Ordinal);
        Assert.Contains("Cannot find module", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timeout_after_the_handshake_keeps_the_output_so_far_and_the_stderr()
    {
        ProcessRunResult run = new(null, string.Empty, "deprecation warning", TimedOut: true);

        ToolResult result = CodeModeOutcome.Compose(
            Settings,
            Facts(logs: "12345\n", helloSeen: true),
            run,
            timeoutSeconds: 20,
            elapsed: TimeSpan.FromSeconds(20)
        );

        Assert.True(result.IsError);
        Assert.Contains("The script timed out after 20s", result.Output, StringComparison.Ordinal);
        Assert.Contains("Output so far:\n12345", result.Output, StringComparison.Ordinal);
        Assert.Contains("deprecation warning", result.Output, StringComparison.Ordinal);
        Assert.True(Structured(result).GetProperty("helloSeen").GetBoolean());
    }

    private static CodeModeRunFacts Facts(
        string logs = "",
        bool helloSeen = false,
        string? protocolError = null,
        bool scriptFailed = false
    ) => new([], logs, Result: null, scriptFailed, helloSeen, protocolError);

    private static JsonElement Structured(ToolResult result) => result.StructuredContent!.Value;
}
