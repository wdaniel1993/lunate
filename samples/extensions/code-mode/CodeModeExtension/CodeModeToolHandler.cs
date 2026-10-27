using System.ComponentModel;
using System.Diagnostics;
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
        var elapsed = Stopwatch.StartNew();
        try
        {
            run = await processRunner
                .RunAsync(
                    new ProcessRunRequest(
                        settings.Runtime,
                        [scriptPath],
                        context.WorkingDirectory,
                        Environment: EnvironmentScrubber.Build(),
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
        finally
        {
            elapsed.Stop();
        }

        return CodeModeOutcome.Compose(
            settings,
            bridge.Snapshot(),
            run,
            timeoutSeconds,
            elapsed.Elapsed
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
