using Lunate.Extensibility;

namespace Lunate.Extensibility.Tests;

internal sealed class FakeExtensionTrustPrompt(bool approve) : IExtensionTrustPrompt
{
    public int Calls { get; private set; }

    public bool? LastFilesChanged { get; private set; }

    public ValueTask<bool> ApproveAsync(
        ExtensionTrustRequest request,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        LastFilesChanged = request.FilesChanged;
        return ValueTask.FromResult(approve);
    }
}
