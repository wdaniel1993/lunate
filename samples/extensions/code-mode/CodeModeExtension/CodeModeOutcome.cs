using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension;

/// <summary>
/// What one run's bridge observed: the captured logs, the script's value and failure flag, and
/// whether the runtime ever spoke the protocol. The result composition reads only this, so every
/// failure path is testable without a child process.
/// </summary>
internal sealed record CodeModeRunFacts(
    IReadOnlyList<BridgedCall> Calls,
    string Logs,
    JsonElement? Result,
    bool ScriptFailed,
    bool HelloSeen,
    string? ProtocolError
);

/// <summary>
/// Turns a finished run into the model-facing result. A run that produced nothing at all is
/// reported as what it is — a runtime that never spoke — with the executable, the elapsed time,
/// the exit code and stderr. Rendering every empty run as "timed out" hides which of the three
/// causes it was: a child that failed to start, one that started but never reached the handshake,
/// or one that ran and stalled.
/// </summary>
internal static class CodeModeOutcome
{
    /// <summary>Composes the teaching text, the error flag and the structured content.</summary>
    public static ToolResult Compose(
        CodeModeSettings settings,
        CodeModeRunFacts facts,
        ProcessRunResult run,
        int timeoutSeconds,
        TimeSpan elapsed
    )
    {
        string output = facts.Logs;
        string stderr = run.Stderr;
        // Anything the runtime wrote before it stopped: the handshake, a log frame, or a protocol
        // violation. Nothing at all is a different failure from a stall after the handshake.
        bool spoke = facts.HelloSeen || output.Length > 0 || facts.ProtocolError is not null;
        bool protocolBroken = facts.ProtocolError is not null || !facts.HelloSeen;
        bool failed = facts.ScriptFailed || (run.ExitCode is { } exitCode && exitCode != 0);

        string text;
        bool isError;
        if (run.TimedOut)
        {
            isError = true;
            text = spoke
                ? TimeoutText(timeoutSeconds, output, stderr, run.ExitCode)
                : SilentTimeoutText(settings, timeoutSeconds, elapsed, stderr, run.ExitCode);
        }
        else if (protocolBroken)
        {
            isError = true;
            string detail = facts.ProtocolError ?? NoHandshakeDetail(run, output);
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"The script runtime broke the codemode protocol: {detail}. Point the extension's 'runtime' setting at a working Node.js.{ProcessSuffix(stderr, run.ExitCode)}"
            );
        }
        else if (failed)
        {
            isError = true;
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"The script failed (exit code {run.ExitCode ?? 0}). Fix the script and retry.\nOutput:\n{output}{ProcessSuffix(stderr, exitCode: null)}"
            );
        }
        else
        {
            isError = false;
            text = facts.Result is { } result
                ? result.ValueKind == JsonValueKind.String
                    ? result.GetString()!
                    : result.GetRawText()
                : output;
        }

        JsonElement structured = JsonSerializer.SerializeToElement(
            new
            {
                exitCode = run.ExitCode,
                timedOut = run.TimedOut,
                helloSeen = facts.HelloSeen,
                elapsedMs = (long)elapsed.TotalMilliseconds,
                result = facts.Result,
                output,
                stderr,
                toolCalls = facts.Calls.Select(call => new
                {
                    callId = call.CallId,
                    tool = call.Tool,
                    isError = call.IsError,
                }),
            }
        );

        return new ToolResult(
            text,
            isError,
            Details: new CodeModeRunDetails(output, stderr),
            StructuredContent: structured
        );
    }

    /// <summary>The runtime spoke and then stopped answering: the script's own deadline.</summary>
    private static string TimeoutText(
        int timeoutSeconds,
        string output,
        string stderr,
        int? exitCode
    ) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The script timed out after {timeoutSeconds}s and its process tree was killed. Simplify the script or raise the timeoutSeconds setting (a call may only tighten it).\nOutput so far:\n{output}{ProcessSuffix(stderr, exitCode)}"
        );

    /// <summary>
    /// The runtime wrote nothing at all before the deadline. The child existed and was alive (a
    /// runtime that is missing or unrunnable throws when it is started, and the handler reports
    /// that separately), so the cause is a spawn the machine could not complete in time, or a
    /// runtime that hangs before the handshake. Say so, and hand over the evidence.
    /// </summary>
    private static string SilentTimeoutText(
        CodeModeSettings settings,
        int timeoutSeconds,
        TimeSpan elapsed,
        string stderr,
        int? exitCode
    ) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The code-mode runtime '{settings.Runtime}' wrote nothing in {elapsed.TotalSeconds:0.0}s: no hello handshake, no output. Its process tree was killed at the {timeoutSeconds}s deadline. A missing runtime fails at once with its own error, so the executable started but stayed silent: the machine was too slow to start it, or the runtime hangs before the handshake. Retry, raise the extension's 'timeoutSeconds' setting, or point 'runtime' at a working Node.js.{ProcessSuffix(stderr, exitCode)}"
        );

    /// <summary>
    /// The runtime exited without saying hello. Naming the exit code separates a runtime that died
    /// on its own (a broken install, a rejected script file) from one that answered with the wrong
    /// protocol.
    /// </summary>
    private static string NoHandshakeDetail(ProcessRunResult run, string output) =>
        run.ExitCode is { } exitCode && output.Length == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"the runtime exited with code {exitCode} without writing anything on stdout"
            )
            : "the runtime produced no hello handshake";

    /// <summary>The exit code and stderr, when there is one to report.</summary>
    private static string ProcessSuffix(string stderr, int? exitCode)
    {
        var suffix = new StringBuilder();
        if (exitCode is { } code)
        {
            suffix.Append("\nexit code: ").Append(code.ToString(CultureInfo.InvariantCulture));
        }

        if (stderr.Length > 0)
        {
            suffix.Append("\nstderr:\n").Append(stderr);
        }

        return suffix.ToString();
    }
}
