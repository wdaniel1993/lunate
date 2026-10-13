using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The outcome of one tool call. <see cref="Output"/> and <see cref="IsError"/> reach the model;
/// <see cref="Details"/> is UI-only (for example a diff) and is never sent.
/// <see cref="StructuredContent"/> is the typed result next to the model-facing text and travels
/// only where the loop sends it; <see cref="Usage"/> is usage the tool reports.
/// <see cref="CallId"/> is the canonical id of the call this result came from, set for nested
/// executions; null for top-level results and for refusals that never ran.
/// </summary>
public sealed record ToolResult(
    string Output,
    bool IsError,
    object? Details = null,
    JsonElement? StructuredContent = null,
    UsageDetails? Usage = null
)
{
    /// <summary>
    /// The canonical id of the call this result came from, set for nested executions
    /// (<see cref="ToolContext.ExecuteToolAsync"/>); null for top-level results (their id is the
    /// one the event stream reported) and for refusals that never ran.
    /// </summary>
    public string? CallId { get; init; }
}
