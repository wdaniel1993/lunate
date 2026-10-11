namespace Lunate.Extensibility.Abstractions;

/// <summary>The result of a tool call as a renderer sees it: text only, so it crosses the JSON boundary.</summary>
public sealed record ToolResultView(
    string ToolName,
    string CallId,
    string ResultText,
    bool IsError
);
