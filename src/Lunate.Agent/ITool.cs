using System.Text.Json;

namespace Lunate.Agent;

/// <summary>
/// A tool the loop can run. The schema is hand-written JSON — never built by reflection — and the
/// loop invokes <see cref="ExecuteAsync"/> directly: Microsoft.Extensions.AI only ever sees the
/// declaration adapter (ADR-0003).
/// </summary>
public interface ITool
{
    /// <summary>The name the model calls the tool by; unique within a registry.</summary>
    string Name { get; }

    /// <summary>The description the model reads.</summary>
    string Description { get; }

    /// <summary>The hand-written JSON schema of the tool's parameters.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>The risk level the approval policy uses.</summary>
    ToolRisk Risk { get; }

    /// <summary>Runs the tool with the model's arguments.</summary>
    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct);
}
