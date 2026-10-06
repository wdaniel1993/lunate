using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

/// <summary>Builders for scripted provider updates.</summary>
internal static class LoopScripts
{
    internal static ChatResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, [new TextContent(text)]);

    internal static ChatResponseUpdate Call(
        string callId,
        string name,
        IDictionary<string, object?>? arguments = null
    ) => new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

    internal static ChatResponseUpdate Calls(params FunctionCallContent[] calls) =>
        new(ChatRole.Assistant, calls);

    /// <summary>A streamed argument fragment in the pipeline's wire shape (T-05).</summary>
    internal static ChatResponseUpdate CallFragment(string callId, string name, string json) =>
        new(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    callId,
                    name,
                    new Dictionary<string, object?> { ["$arguments"] = json }
                ),
            ]
        );

    internal static ChatResponseUpdate Stop() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Stop };

    internal static ChatResponseUpdate ToolCalls() =>
        new(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ToolCalls };

    internal static Dictionary<string, object?> Args(params (string Key, object? Value)[] entries)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string key, object? value) in entries)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
