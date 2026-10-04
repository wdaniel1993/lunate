namespace Lunate.Ai;

public sealed record ModelInfo(string Id, string Provider, Uri? Endpoint, int ContextWindow, bool SupportsTools);
