namespace Lunate.Extensibility;

/// <summary>
/// One project-extension trust question: the descriptor to trust plus whether this repository has
/// trusted the extension before (its files changed since), so the prompt can say so.
/// </summary>
public sealed record ExtensionTrustRequest(ExtensionDescriptor Descriptor, bool FilesChanged);
