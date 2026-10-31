using System.Text.Json;

namespace Lunate.Agent;

/// <summary>
/// A tool the loop can run. The schema is hand-written JSON — never built by reflection — and the
/// loop invokes <see cref="ExecuteAsync"/> directly: Microsoft.Extensions.AI only ever sees the
/// declaration adapter (ADR-0003). The extra members are default interface members, so existing
/// tools compile unchanged and new tools opt in.
/// </summary>
public interface ITool
{
    /// <summary>The name the model calls the tool by; unique within a registry.</summary>
    string Name { get; }

    /// <summary>The description the model reads.</summary>
    string Description { get; }

    /// <summary>The hand-written JSON schema of the tool's parameters.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>Where the tool is visible; see <see cref="ToolExposure"/>.</summary>
    ToolExposure Exposure => ToolExposure.Direct;

    /// <summary>The grouping the tool belongs to; null when ungrouped.</summary>
    ToolNamespace? Namespace => null;

    /// <summary>The tool's MCP-style annotations; null when none are declared.</summary>
    ToolAnnotations? Annotations => null;

    /// <summary>The hand-written JSON schema of the structured result; null for text-only tools.</summary>
    JsonElement? OutputSchema => null;

    /// <summary>The tool's scheduling intent for the future parallel executor.</summary>
    ToolConcurrency Concurrency => ToolConcurrency.Parallel;

    /// <summary>
    /// The risk level the approval policy uses. Tools with special needs override this explicitly;
    /// otherwise it derives from <see cref="Annotations"/>.
    /// </summary>
    ToolRisk Risk => ToolRiskResolver.FromAnnotations(Annotations);

    /// <summary>Runs the tool with the model's arguments.</summary>
    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct);
}
