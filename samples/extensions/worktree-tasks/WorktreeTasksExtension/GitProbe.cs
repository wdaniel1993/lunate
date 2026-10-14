using System.ComponentModel;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>The outcome of one git call: the exit code and output when it ran, else a failure line.</summary>
internal sealed record GitProbeResult(int? ExitCode, string Stdout, string Stderr, string? Failure)
{
    /// <summary>True when the process ran and exited zero.</summary>
    public bool Succeeded => Failure is null && ExitCode == 0;

    /// <summary>The combined stdout and stderr, trimmed; the process output when it ran.</summary>
    public string Output => Failure ?? (Stdout + Stderr).Trim();

    /// <summary>A short line for teaching errors; never empty.</summary>
    public string Detail =>
        Output.Length > 0 ? Output : $"git exited with code {(ExitCode?.ToString() ?? "?")}";
}

/// <summary>
/// Runs git through the core/process service from a working directory. The extension holds the
/// service as a seam: without it every call refuses with a teaching error, nothing throws.
/// </summary>
internal sealed class GitProbe(IProcessRunner? runner)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public bool Available => runner is not null;

    public async Task<GitProbeResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken ct
    )
    {
        if (runner is null)
        {
            return new GitProbeResult(
                null,
                string.Empty,
                string.Empty,
                "the host did not provide the core/process service, so git cannot run. Update Lunate and retry."
            );
        }

        try
        {
            ProcessRunResult result = await runner
                .RunAsync(
                    new ProcessRunRequest("git", arguments, workingDirectory, Timeout: Timeout),
                    ct
                )
                .ConfigureAwait(false);
            return new GitProbeResult(result.ExitCode, result.Stdout, result.Stderr, null);
        }
        catch (OperationCanceledException)
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
            return new GitProbeResult(
                null,
                string.Empty,
                string.Empty,
                $"git could not start: {exception.Message}. Install git and retry."
            );
        }
    }
}
