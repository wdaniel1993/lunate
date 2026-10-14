using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension;

/// <summary>The full (capped) run output for the UI-only details slot.</summary>
internal sealed record CodeModeRunDetails(string Output, string Stderr);

/// <summary>
/// The <c>codemode__run</c> handler: composes the script (runtime + stub prelude + model code),
/// writes it to a temp file cleaned in a finally, and runs it through <c>core/process</c> with a
/// scrubbed environment allow-list. The child's call frames are bridged into the core pipeline by
/// <see cref="CodeModeBridge"/>; every limit ends in a teaching error.
/// </summary>
internal sealed class CodeModeToolHandler(CodeModeSettings settings, IProcessRunner? runner)
    : IExtensionToolHandler
{
    /// <summary>
    /// The child's whole environment: an explicit allow-list, never Lunate's own. Provider keys
    /// and other host variables cannot leak into model-written code.
    /// </summary>
    private static readonly string[] EnvironmentAllowList =
    [
        "PATH",
        "HOME",
        "TMPDIR",
        "TEMP",
        "TMP",
        "SystemRoot",
        "USERPROFILE",
    ];

    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (
            arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("code", out JsonElement codeElement)
            || codeElement.ValueKind != JsonValueKind.String
        )
        {
            return Error(
                "code is required and must be a string of JavaScript. Fix the arguments and retry."
            );
        }

        if (!TryResolveTimeout(arguments, out int timeoutSeconds, out string timeoutError))
        {
            return Error(timeoutError);
        }

        if (runner is null)
        {
            return Error(
                "The host did not provide the core/process service, so the script runtime cannot start. Update Lunate and retry."
            );
        }

        if (context.ExecuteToolAsync is null)
        {
            return Error(
                "codemode__run only runs inside an agent run; there is no tool pipeline to bridge to."
            );
        }

        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            "lunate-codemode-" + Guid.NewGuid().ToString("N") + ".js"
        );
        try
        {
            await File.WriteAllTextAsync(
                    scriptPath,
                    CodeModeScript.Compose(context.ToolStubs ?? [], codeElement.GetString()!),
                    ct
                )
                .ConfigureAwait(false);
            return await RunAsync(scriptPath, timeoutSeconds, context, runner, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    private async Task<ToolResult> RunAsync(
        string scriptPath,
        int timeoutSeconds,
        ToolContext context,
        IProcessRunner processRunner,
        CancellationToken ct
    )
    {
        var bridge = new CodeModeBridge(context, settings);
        ProcessRunResult run;
        try
        {
            run = await processRunner
                .RunAsync(
                    new ProcessRunRequest(
                        settings.Runtime,
                        [scriptPath],
                        context.WorkingDirectory,
                        Environment: ScrubbedEnvironment(),
                        Timeout: TimeSpan.FromSeconds(timeoutSeconds)
                    )
                    {
                        LineHandler = bridge.OnLineAsync,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is Win32Exception
                        or InvalidOperationException
                        or PlatformNotSupportedException
                        or ArgumentException
                        or IOException
            )
        {
            return Error(
                $"Could not start the code-mode runtime '{settings.Runtime}': {exception.Message}. Install Node.js or point the extension's 'runtime' setting at one; the call was not run."
            );
        }

        return ComposeResult(bridge, run, timeoutSeconds);
    }

    private ToolResult ComposeResult(
        CodeModeBridge bridge,
        ProcessRunResult run,
        int timeoutSeconds
    )
    {
        string output = bridge.Logs;
        string stderr = run.Stderr;
        bool protocolBroken = bridge.ProtocolError is not null || !bridge.HelloSeen;
        bool failed = bridge.ScriptFailed || (run.ExitCode is { } code && code != 0);

        string text;
        bool isError;
        if (run.TimedOut)
        {
            isError = true;
            text =
                $"The script timed out after {timeoutSeconds.ToString(CultureInfo.InvariantCulture)}s and its process tree was killed. Simplify the script or raise the timeoutSeconds setting (a call may only tighten it).\nOutput so far:\n{output}";
        }
        else if (protocolBroken)
        {
            isError = true;
            string detail = bridge.ProtocolError ?? "the runtime produced no hello handshake";
            text =
                $"The script runtime broke the codemode protocol: {detail}. Point the extension's 'runtime' setting at a working Node.js.{StderrSuffix(stderr)}";
        }
        else if (failed)
        {
            isError = true;
            text =
                $"The script failed (exit code {(run.ExitCode ?? 0).ToString(CultureInfo.InvariantCulture)}). Fix the script and retry.\nOutput:\n{output}{StderrSuffix(stderr)}";
        }
        else
        {
            isError = false;
            text = bridge.Result is { } result
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
                result = bridge.Result,
                output,
                stderr,
                toolCalls = bridge.Calls.Select(call => new
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

    private bool TryResolveTimeout(JsonElement arguments, out int timeoutSeconds, out string error)
    {
        timeoutSeconds = settings.TimeoutSeconds;
        error = string.Empty;
        if (!arguments.TryGetProperty("timeoutSeconds", out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int requested))
        {
            error =
                "timeoutSeconds must be a whole number of seconds. Fix the arguments and retry.";
            return false;
        }

        timeoutSeconds = settings.ClampTimeout(requested);
        return true;
    }

    private static IReadOnlyDictionary<string, string> ScrubbedEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in EnvironmentAllowList)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static string StderrSuffix(string stderr) =>
        stderr.Length > 0 ? $"\nstderr:\n{stderr}" : string.Empty;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The runtime may still hold the file briefly (Windows); the OS temp dir cleans up.
        }
        catch (UnauthorizedAccessException)
        {
            // Same; cleanup is best effort.
        }
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
