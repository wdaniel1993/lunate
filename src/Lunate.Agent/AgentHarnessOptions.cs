namespace Lunate.Agent;

/// <summary>Configuration for an <see cref="AgentHarness"/>.</summary>
public sealed record AgentHarnessOptions
{
    /// <summary>The maximum number of model calls per run.</summary>
    public int MaxSteps { get; init; } = 50;

    /// <summary>The system prompt sent before the history; null sends no system message.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The directory tools run in.</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>The approval seam; null allows every tool call.</summary>
    public IToolApprover? Approver { get; init; }
}
