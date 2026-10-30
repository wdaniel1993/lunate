using System.Globalization;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

internal static class WriteVerificationPipeline
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static async Task<WriteVerificationOutcome?> RefuseIfNeededAsync(
        Func<string, IWriteVerifier?>? verifierSelector,
        string relativePath,
        string baselineContent,
        string stagedContent,
        WorkspaceInfo workspace,
        CancellationToken ct,
        TimeProvider? timeProvider = null
    )
    {
        IWriteVerifier? verifier = verifierSelector?.Invoke(relativePath);
        if (verifier is null)
        {
            return null;
        }

        TimeProvider clock = timeProvider ?? TimeProvider.System;
        long started = clock.GetTimestamp();
        using CancellationTokenSource timeout = new();
        using CancellationTokenSource delayCancellation = new();
        using CancellationTokenSource verifierCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        Task<WriteVerificationResult> verification = verifier
            .VerifyAsync(
                new WriteVerificationRequest(relativePath, baselineContent, stagedContent, workspace),
                verifierCancellation.Token
            )
            .AsTask();
        Task deadline = Task.Delay(Deadline, clock, delayCancellation.Token);
        Task completed = await Task.WhenAny(verification, deadline).ConfigureAwait(false);

        if (completed == deadline)
        {
            ct.ThrowIfCancellationRequested();
            timeout.Cancel();
            _ = verification.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

            TimeSpan elapsed = clock.GetElapsedTime(started);
            string duration = elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
            return new(
                Notice: FormattableString.Invariant(
                    $"unverified after {duration} s: verification timeout after {Deadline.TotalSeconds:F0} s"
                )
            );
        }

        delayCancellation.Cancel();
        WriteVerificationResult result = await verification.ConfigureAwait(false);

        return result.Status is WriteVerificationStatus.Refused
            ? new WriteVerificationOutcome(
                Refusal: new ToolResult(
                    result.Reason
                        ?? "The write verifier refused the staged content; review its structured diagnostics and correct the errors before retrying.",
                    IsError: true,
                    StructuredContent: result.StructuredContent
                )
            )
            : null;
    }
}

internal sealed record WriteVerificationOutcome(ToolResult? Refusal = null, string? Notice = null);
