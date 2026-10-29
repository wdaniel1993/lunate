using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// One extension the host composition supplies out of process lifetime: the parsed manifest plus
/// the factory to run for it, in declaration order. Its code lives in the host's own assemblies —
/// the default <c>AssemblyLoadContext</c> — so a built-in declares neither an entry assembly nor an
/// out-of-process runtime, and it is never installed, trusted by content hash, reloaded or
/// unloaded; session end stops its background services.
/// </summary>
/// <param name="ManifestPath">Where the host read the manifest from; refusals name it.</param>
/// <param name="Manifest">The parsed manifest; it must itself declare <c>builtin: true</c>.</param>
/// <param name="Factory">Creates the extension against a normal extension context.</param>
public sealed record BuiltInExtension(
    string ManifestPath,
    ExtensionManifest Manifest,
    Func<IExtensionFactory> Factory
);
