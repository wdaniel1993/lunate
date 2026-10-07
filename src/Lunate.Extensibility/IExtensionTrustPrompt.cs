namespace Lunate.Extensibility;

public interface IExtensionTrustPrompt
{
    ValueTask<bool> ApproveAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    );
}
