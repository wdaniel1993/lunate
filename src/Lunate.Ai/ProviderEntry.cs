namespace Lunate.Ai;

/// <summary>
/// A named user-owned provider access entry. Credential references point to a secret source; they
/// never contain the resolved credential value.
/// </summary>
public sealed record ProviderEntry(
    string Name,
    string Kind,
    Uri BaseUrl,
    string CredentialReference,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Region = null
);
