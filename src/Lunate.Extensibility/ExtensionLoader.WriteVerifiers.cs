using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private void CrossCheckManifestWriteVerifiers(ExtensionDescriptor descriptor)
    {
        IReadOnlyList<string> registered = _writeVerifiers.RegisteredNames(descriptor.Id);
        HashSet<string> declared = new(
            descriptor.Manifest.WriteVerifiers.Select(verifier => verifier.Name),
            StringComparer.Ordinal
        );

        foreach (string name in registered)
        {
            if (!declared.Contains(name))
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': write verifier '{name}' is registered by code but not declared in the manifest 'writeVerifiers' list; declare it or remove the registration."
                );
            }
        }

        foreach (WriteVerifierDeclaration declaration in descriptor.Manifest.WriteVerifiers)
        {
            if (!registered.Contains(declaration.Name, StringComparer.Ordinal))
            {
                (_options.Log ?? NullExtensionLog.Instance).Warn(
                    $"extension '{descriptor.Id}': manifest declares write verifier '{declaration.Name}' but no registration matched; the verifier is omitted."
                );
            }
        }
    }
}
