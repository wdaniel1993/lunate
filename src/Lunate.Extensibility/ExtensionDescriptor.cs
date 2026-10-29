using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed record ExtensionDescriptor(
    string Id,
    string Version,
    ExtensionScope Scope,
    /// <summary>The extension's own folder; <see langword="null"/> for a built-in, whose code
    /// lives in the host's own assemblies (the default load context).</summary>
    string? Directory,
    ExtensionManifest Manifest
);
