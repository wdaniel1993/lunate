using Lunate.Agent;

namespace Lunate.Extensibility.Testing;

/// <summary>The events of one run and the session file they were mirrored into.</summary>
public sealed record ExtensionRunResult(IReadOnlyList<AgentEvent> Events, string SessionPath);
