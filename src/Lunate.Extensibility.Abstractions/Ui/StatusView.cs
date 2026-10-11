namespace Lunate.Extensibility.Abstractions;

/// <summary>The session status as a widget sees it: model, token usage, directory and branch.</summary>
public sealed record StatusView(
    string Model,
    long TokensUsed,
    long ContextWindow,
    string WorkingDirectory,
    string? GitBranch
);
