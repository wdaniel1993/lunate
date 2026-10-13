using System.Text.Json;
using Lunate.Agent;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// The handler behind one extension tool. It receives the same <see cref="ToolContext"/> and returns
/// the same <see cref="ToolResult"/> as core tools — one tool model, no mirrored context.
/// </summary>
public interface IExtensionToolHandler
{
    /// <summary>Runs the tool with the model's arguments and the run's context.</summary>
    Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken ct);
}

/// <summary>
/// One tool an extension contributes. Registration is registration only: it must not start
/// processes, sockets or timers; the registered tool enters the session's tool registry at
/// composition and behaves like a core tool there.
/// </summary>
public sealed record ExtensionToolRegistration
{
    /// <summary>
    /// The tool's local name; the model-facing name is <c>&lt;extension-id&gt;__&lt;name&gt;</c>,
    /// sanitized to <c>[A-Za-z0-9_-]</c> and at most 64 characters. The manifest declares this
    /// local name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The description the model reads.</summary>
    public required string Description { get; init; }

    /// <summary>The hand-written JSON schema of the tool's parameters; must be a JSON object.</summary>
    public required JsonElement ParametersSchema { get; init; }

    /// <summary>The hand-written JSON schema of the structured result; null for text-only tools.</summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>
    /// The tool's annotations; null means no hints, and the approval policy treats the tool as not
    /// read-only and possibly destructive (the MCP default), so the call asks.
    /// </summary>
    public ToolAnnotations? Annotations { get; init; }

    /// <summary>Where the tool is visible; declared to the model and nested-callable by default.</summary>
    public AgentToolExposure Exposure { get; init; } = AgentToolExposure.Direct;

    /// <summary>An explicit risk override for special needs; null derives it from the annotations.</summary>
    public ToolRisk? Risk { get; init; }

    /// <summary>The handler the loop invokes with the model's arguments and the run's context.</summary>
    public required IExtensionToolHandler Handler { get; init; }
}
