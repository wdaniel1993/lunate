using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed record LoadedExtension(
    string Id,
    IExtension Extension,
    ExtensionDescriptor Descriptor
);
