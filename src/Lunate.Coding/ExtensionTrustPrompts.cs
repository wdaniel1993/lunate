using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>Asks one trust question in the session's live area; true approves.</summary>
internal delegate ValueTask<bool> TrustQuestion(
    string title,
    IReadOnlyList<string> details,
    CancellationToken cancellationToken
);

/// <summary>
/// The interactive extension trust prompt: it builds the question (id, version, scope and
/// directory; saying so when the files changed since this repository trusted the extension) and
/// hands it to the session's live-area question path. The trust store records an approval once per
/// repository and content hash, so this is only reached when nothing has approved the content.
/// </summary>
internal sealed class TuiExtensionTrustPrompt(TrustQuestion ask) : IExtensionTrustPrompt
{
    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ExtensionDescriptor descriptor = request.Descriptor;
        string title = request.FilesChanged
            ? $"Load project extension '{descriptor.Id}' {descriptor.Version} from {descriptor.Directory} — its files changed since this repository trusted it. Load the new version"
            : $"Load project extension '{descriptor.Id}' {descriptor.Version} from {descriptor.Directory}";
        return ask(title, [], cancellationToken);
    }
}

/// <summary>
/// The print- and ACP-mode trust prompt: repository code is never auto-approved off a terminal. It
/// refuses, logs why, and points at the interactive path that can approve it.
/// </summary>
internal sealed class NonInteractiveTrustPrompt : IExtensionTrustPrompt
{
    private readonly IExtensionLog _log;

    internal NonInteractiveTrustPrompt(IExtensionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _log.Warn(
            $"project extension '{request.Descriptor.Id}' was not trusted; run an interactive session to approve it."
        );
        return ValueTask.FromResult(false);
    }
}
