namespace Lunate.Coding;

/// <summary>
/// The masked secret input behind <c>/login</c>. The TUI prompt implements it; tests replace it
/// with a scripted one. The returned value never appears in a notice, history, scrollback or the
/// session file, and <c>null</c> means the person cancelled.
/// </summary>
internal interface ISecretPrompt
{
    Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken);
}
