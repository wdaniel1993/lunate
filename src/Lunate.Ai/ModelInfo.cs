namespace Lunate.Ai;

/// <summary>
/// A model in the catalog. <see cref="AuthRef"/> optionally names a key in the user's auth store:
/// the deliberate opt-in that lets that key reach a declared (custom) endpoint.
/// </summary>
public sealed record ModelInfo(
    string Id,
    string Provider,
    Uri? Endpoint,
    int ContextWindow,
    bool SupportsTools,
    string? AuthRef = null
);
