using Lunate.Agent;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// <c>/copy</c> and <c>Ctrl+X</c>: the last completed answer to the clipboard, through the
    /// transport the session's location allows. A read-only command, so it stays available while a
    /// run is active — but the turn in flight is not yet a completed answer, so the previous one is
    /// what gets copied.
    /// </summary>
    private async Task CopyLastAnswerAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CopyMessage> messages = _session is { } session
            ? CopyAnswer.FromEntries(session.Entries)
            : CopyAnswer.FromTranscript(_transcript);
        if (CopyAnswer.Last(messages, _lastTurnCompleted) is not { } answer)
        {
            Notify(ClipboardNotice.Nothing);
            return;
        }

        try
        {
            ITextClipboard clipboard =
                _options.TextClipboard
                ?? new ClipboardWriter(_environment, writeTerminal: _console.Write);
            ClipboardCopy copy = await clipboard
                .WriteAsync(answer, cancellationToken)
                .ConfigureAwait(false);
            Notify(ClipboardNotice.Text(copy));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Notify(ClipboardNotice.Failed(exception.Message), NoticeLevel.Error);
        }
    }
}
