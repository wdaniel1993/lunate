namespace Lunate.Agent;

/// <summary>
/// The typed description of one tool, derived from its JSON schemas by <see cref="ToolStubs"/>:
/// what a code-mode runtime (or any other consumer) renders into its language. The core emits
/// typed data only; rendering stubs into a language is the consumer's job.
/// </summary>
/// <param name="Name">The model-facing tool name.</param>
/// <param name="Description">The description the model-facing declaration carried.</param>
/// <param name="Parameters">The typed parameters in schema declaration order; empty without one.</param>
/// <param name="HasStructuredOutput">True when the tool declares an <c>OutputSchema</c>.</param>
public sealed record ToolStub(
    string Name,
    string Description,
    IReadOnlyList<ToolStubParameter> Parameters,
    bool HasStructuredOutput
);
