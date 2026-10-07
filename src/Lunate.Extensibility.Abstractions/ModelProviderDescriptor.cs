namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A model provider declared in the manifest. The registry lists declarations; instantiating
/// clients and routing land with the model-router card. <see cref="SecretName"/> names an entry in
/// the extension's secrets store; secret values never cross this boundary.
/// </summary>
public sealed record ModelProviderDescriptor(
    string Id,
    string DisplayName,
    string Endpoint,
    string SecretName,
    IReadOnlyList<string> ModelIds
);
