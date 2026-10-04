namespace S5.Harness;

public sealed record LiveAreaView
{
    public IReadOnlyList<string> Tail { get; init; } = [];

    public string? SpinnerGlyph { get; init; }

    public string Footer { get; init; } = string.Empty;

    public string? ApprovalPrompt { get; init; }

    public IReadOnlyList<string> QueuedSteering { get; init; } = [];

    public string Input { get; init; } = string.Empty;

    public string? Status { get; init; }
}
