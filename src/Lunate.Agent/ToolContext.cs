namespace Lunate.Agent;

/// <summary>What a tool knows about the run it executes in.</summary>
public sealed record ToolContext(string WorkingDirectory, IAgentEvents Events);
