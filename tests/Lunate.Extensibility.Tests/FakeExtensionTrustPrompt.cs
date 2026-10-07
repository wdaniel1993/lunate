using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal sealed class FakeExtensionTrustPrompt(bool approve) : IExtensionTrustPrompt
{
    public int Calls { get; private set; }

    public ValueTask<bool> ApproveAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        return ValueTask.FromResult(approve);
    }
}
