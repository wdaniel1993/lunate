namespace Lunate.Extensibility;

public interface IExtensionTrustPrompt
{
    ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    );
}
