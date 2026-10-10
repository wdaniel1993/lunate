namespace Lunate.Tui;

/// <summary>
/// The session status shown as the footer line: model id, tokens used against the context window,
/// working directory (pre-shortened by the wiring layer) and the current git branch when known.
/// </summary>
public sealed record StatusFooterModel(
    string Model,
    long TokensUsed,
    long ContextWindow,
    string WorkingDirectory,
    string? GitBranch
);
