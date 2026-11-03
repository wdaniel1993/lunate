namespace Lunate.Ai.SystemOne;

internal sealed record SystemOnePreset(
    Uri Endpoint,
    IReadOnlyDictionary<string, string>? ImageHeaders = null
);
