using System.Text;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The live-area masked secret prompt behind <c>/login</c>. It is a state machine fed by the key
/// pump: <see cref="ReadAsync"/> opens the prompt and returns a pending task, while
/// <see cref="HandleKey"/> edits a plaintext buffer and posts only its mask to the live area.
/// Awaiting a console read on the key pump would deadlock, so nothing here waits for a key.
/// One lock guards the state because the key pump and a cancellation registration run on
/// different threads; no transition awaits inside it.
/// </summary>
internal sealed class TuiSecretPrompt(LiveArea live) : ISecretPrompt
{
    private readonly Lock _gate = new();
    private readonly StringBuilder _buffer = new();
    private TaskCompletionSource<string?>? _pending;
    private string _label = string.Empty;
    private bool _masked = true;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public async Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken) =>
        await ReadCoreAsync(prompt, masked: true, cancellationToken).ConfigureAwait(false);

    public async Task<string?> ReadTextAsync(string prompt, CancellationToken cancellationToken) =>
        await ReadCoreAsync(prompt, masked: false, cancellationToken).ConfigureAwait(false);

    private async Task<string?> ReadCoreAsync(
        string prompt,
        bool masked,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        TaskCompletionSource<string?> pending;
        lock (_gate)
        {
            // Only one prompt can run: the prompt is modal, so a second read never opens a second one.
            if (_pending is not null)
            {
                return null;
            }

            pending = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _pending = pending;
            _label = prompt;
            _masked = masked;
            _buffer.Clear();
            ShowContent();
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            Cancel(pending, cancellationToken)
        );
        return await pending.Task.ConfigureAwait(false);
    }

    public bool HandleKey(KeyEvent key)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                return false;
            }

            switch (key)
            {
                case {
                    Kind: KeyKind.Character,
                    Ctrl: false,
                    Alt: false,
                    Text: { Length: > 0 } text
                }:
                    Append(text);
                    return true;
                case { Kind: KeyKind.Space, Ctrl: false, Alt: false }:
                    Append(" ");
                    return true;
                case { Kind: KeyKind.Paste }:
                    // A secret keeps only its non-whitespace characters; a trailing newline is
                    // stripped, never treated as Enter. A visible field keeps its spaces; Enter
                    // remains the only way to submit either.
                    Append(_masked ? WithoutWhitespace(key.Text) : WithoutNewlines(key.Text));
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
    }

    private static string WithoutWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var stripped = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                stripped.Append(character);
            }
        }

        return stripped.ToString();
    }

    private static string WithoutNewlines(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r", string.Empty).Replace("\n", string.Empty);

    private void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _buffer.Append(text);
        ShowContent();
    }

    private void Backspace()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _buffer.Length--;
        ShowContent();
    }

    /// <summary>The live state carries only the display text: a mask for a secret, the text itself
    /// for every visible prompt.</summary>
    private void ShowContent() =>
        live.SetSecretPrompt(
            new SecretPromptState(
                _label,
                _masked ? new string('*', _buffer.Length) : _buffer.ToString()
            )
        );

    private void Complete(string? value)
    {
        TaskCompletionSource<string?>? pending = Detach();
        live.SetSecretPrompt(null);
        pending?.TrySetResult(value);
    }

    private void Cancel(TaskCompletionSource<string?> pending, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_pending, pending))
            {
                return;
            }

            Detach();
            live.SetSecretPrompt(null);
        }

        pending.TrySetCanceled(cancellationToken);
    }

    /// <summary>Clears the open prompt; the caller holds <see cref="_gate"/>.</summary>
    private TaskCompletionSource<string?>? Detach()
    {
        TaskCompletionSource<string?>? pending = _pending;
        _pending = null;
        _label = string.Empty;
        _masked = true;
        _buffer.Clear();
        return pending;
    }
}
