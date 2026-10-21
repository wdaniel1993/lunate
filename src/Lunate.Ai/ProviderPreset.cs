namespace Lunate.Ai;

/// <summary>
/// A provider preset: the base URL and key variable that make a provider usable without a
/// hand-written model entry. <see cref="KeyVariableAlias"/> is an optional second environment
/// variable accepted for the same provider (for example <c>GEMINI_API_KEY</c> for Google).
/// </summary>
public sealed record ProviderPreset(
    string Id,
    string Name,
    ProviderAdapter Adapter,
    Uri BaseUrl,
    string KeyVariable,
    string? KeyVariableAlias = null
);
