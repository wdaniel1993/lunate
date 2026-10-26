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

        string title = TrustFormat.ExtensionQuestion(request.Descriptor, request.FilesChanged);
        return ask(title, TrustFormat.ProvenanceLine(request.FromPackage), cancellationToken);
    }

    /// <summary>
    /// The single-file question through the same live-area prompt as a packaged extension: one
    /// surface, one answer path. The wording comes from <see cref="SingleFileTrustQuestion"/>, so
    /// the interactive prompt and its tests cannot drift apart.
    /// </summary>
    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ask(
            SingleFileTrustQuestion.Title(request),
            SingleFileTrustQuestion.Details(request),
            cancellationToken
        );
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
            $"project extension '{request.Descriptor.Id}' was not trusted; run an interactive session or 'lunate trust' to approve it."
        );
        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// A single-file extension is never approved without a terminal either: the message names the
    /// file and the way to approve it, and the answer is no.
    /// </summary>
    public ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _log.Warn(
            $"single-file extension '{request.Id}' at '{request.Path}' was not trusted; run an interactive session to approve it."
        );
        return ValueTask.FromResult(false);
    }
}
