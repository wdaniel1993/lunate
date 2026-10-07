using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>The aggregate project-trust decision; null when no handler is registered.</summary>
public sealed record ProjectTrustDispatch(int HandlerCount, ProjectTrustResult? Result)
{
    /// <summary>Whether any handler participated in the decision.</summary>
    public bool HasHandlers => HandlerCount > 0;
}

/// <summary>The accepted prompt-section edits and the final active-tool selection.</summary>
public sealed record RunStartingDispatch(
    IReadOnlyList<PromptSectionEdit> SectionEdits,
    IReadOnlyList<string>? ActiveTools
);

/// <summary>The merged, source-tagged context additions after the per-extension budget.</summary>
public sealed record ContextBuildingDispatch(IReadOnlyList<ContextMessage> AddedMessages);

/// <summary>The composed tool-result text and the last attached JSON data.</summary>
public sealed record ToolResultReadyDispatch(string Output, JsonElement? Data);

/// <summary>The collected turn entries and whether one continuation was granted.</summary>
public sealed record TurnEndedDispatch(
    IReadOnlyList<TurnEndedEntry> Entries,
    bool RequestContinuation
);
