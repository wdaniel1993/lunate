using System.Text;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The live-area masked secret prompt behind <c>/login</c>. It is a state machine fed by the key
/// pump: <see cref="ReadAsync"/> opens the prompt and returns a pending task, while
/// <see cref="HandleKey"/> edits a plaintext buffer and posts only its mask to the live area.
/// Awaiting a console read on the key pump would deadlock, so nothing here waits for a key.
/// </summary>
internal sealed class TuiSecretPrompt(LiveArea live) : ISecretPrompt
{
    private readonly StringBuilder _buffer = new();
    private TaskCompletionSource<string?>? _pending;
    private string _label = string.Empty;

    public bool IsOpen => _pending is not null;

    public async Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        // Only one login can run: the prompt is modal, so a second read never opens a second one.
        if (_pending is not null || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var pending = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending = pending;
        _label = prompt;
        _buffer.Clear();
        live.SetSecretPrompt(new SecretPromptState(prompt, string.Empty));
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            Cancel(pending, cancellationToken)
        );
        return await pending.Task.ConfigureAwait(false);
    }

    public bool HandleKey(KeyEvent key)
    {
        if (_pending is null)
        {
            return false;
        }

        switch (key)
        {
            case { Kind: KeyKind.Character, Ctrl: false, Alt: false, Text: { Length: > 0 } text }:
                Append(text);
                return true;
            case { Kind: KeyKind.Space, Ctrl: false, Alt: false }:
                Append(" ");
                return true;
            case { Kind: KeyKind.Backspace }:
                Backspace();
                return true;
            case { Kind: KeyKind.Enter }:
                Complete(_buffer.ToString());
                return true;
            case { Kind: KeyKind.Escape }:
                Complete(null);
                return true;
            default:
                return false;
        }
    }

    private void Append(string text)
    {
        _buffer.Append(text);
        ShowMask();
    }

    private void Backspace()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _buffer.Length--;
        ShowMask();
    }

    /// <summary>The live state carries only the mask, never the typed characters.</summary>
    private void ShowMask() =>
        live.SetSecretPrompt(new SecretPromptState(_label, new string('*', _buffer.Length)));

    private void Complete(string? value)
    {
        TaskCompletionSource<string?>? pending = Detach();
        live.SetSecretPrompt(null);
        pending?.TrySetResult(value);
    }

    private void Cancel(TaskCompletionSource<string?> pending, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        Detach();
        live.SetSecretPrompt(null);
        pending.TrySetCanceled(cancellationToken);
    }

    private TaskCompletionSource<string?>? Detach()
    {
        TaskCompletionSource<string?>? pending = _pending;
        _pending = null;
        _label = string.Empty;
        _buffer.Clear();
        return pending;
    }
}
