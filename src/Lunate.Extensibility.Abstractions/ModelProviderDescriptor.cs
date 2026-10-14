namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A model provider declared in the manifest. The registry lists declarations and instantiates
/// them lazily into OpenAI-compatible clients on first use. <see cref="SecretName"/> names an entry
/// in the extension's secrets store; the host reads the value and it only ever reaches the client's
/// request headers — hygiene for declared providers, not a security boundary (ADR-0017).
/// </summary>
/// <param name="Id">The provider's local id; published namespaced by the registry.</param>
/// <param name="DisplayName">The name shown in listings.</param>
/// <param name="Endpoint">The provider's absolute http(s) endpoint.</param>
/// <param name="SecretName">The secrets-store entry the host reads; never a value.</param>
/// <param name="ModelIds">The model ids the provider serves.</param>
/// <param name="ContextWindow">The provider's context window in tokens; required, positive.</param>
/// <param name="SupportsTools">Whether the provider supports tool calls; default true.</param>
public sealed record ModelProviderDescriptor(
    string Id,
    string DisplayName,
    string Endpoint,
    string SecretName,
    IReadOnlyList<string> ModelIds,
    int ContextWindow,
    bool SupportsTools = true
);
