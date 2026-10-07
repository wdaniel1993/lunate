using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed record ExtensionDescriptor(
    string Id,
    string Version,
    ExtensionScope Scope,
    string Directory,
    ExtensionManifest Manifest
);
