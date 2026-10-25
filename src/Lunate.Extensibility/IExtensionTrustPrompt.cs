namespace Lunate.Extensibility;

/// <summary>
/// The one trust surface of an extension host, packaged and single-file alike. A single-file
/// question is asked through the same implementation and the same live-area prompt as a packaged
/// extension's, so there is one place a user can approve code and one place that refuses when
/// nothing can be asked. Both members default to refusing the question when an implementation does
/// not decide: a trust decision is never granted by omission.
/// </summary>
public interface IExtensionTrustPrompt
{
    /// <summary>The packaged-extension question; true approves.</summary>
    ValueTask<bool> ApproveAsync(ExtensionTrustRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    /// <summary>The single-file question; true approves. Never true without a user's answer.</summary>
    ValueTask<bool> ApproveSingleFileAsync(
        SingleFileTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
