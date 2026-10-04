using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Spike.Shared;

public static class Updates
{
    public static ScriptedUpdate Text(string text) =>
        new(new ChatResponseUpdate(ChatRole.Assistant, text));

    public static ScriptedUpdate Call(string callId, string name, string argumentsJson) =>
        new(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, name, ParseArguments(argumentsJson))]));

    public static ScriptedUpdate CallWithSideEffect(string callId, string name, string argumentsJson, Action afterYield) =>
        new(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, name, ParseArguments(argumentsJson))]), afterYield);

    public static IReadOnlyList<ScriptedUpdate> SplitCall(string callId, string name, params string[] fragments)
    {
        return fragments
            .Select(fragment => new ScriptedUpdate(new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(callId, name, new Dictionary<string, object?> { ["$raw"] = fragment })])))
            .ToList();
    }

    public static ScriptedUpdate Finish(ChatFinishReason? reason = null) =>
        new(new ChatResponseUpdate { FinishReason = reason ?? ChatFinishReason.Stop });

    public static ScriptedUpdate Usage(int inputTokens, int outputTokens) =>
        new(new ChatResponseUpdate(
            ChatRole.Assistant,
            [new UsageContent(new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens })]));

    private static Dictionary<string, object?> ParseArguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
    }
}

public static class ChatScripts
{
    public const string SystemUserMessage = "Fix the calculator bug in src/Calculator.cs.";

    public static IReadOnlyList<ScriptedTurn> Canonical() =>
    [
        new(
        [
            Updates.Text("I'll read the file first."),
            Updates.Call("call_read_1", "read", """{"path":"src/Calculator.cs"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("I found the bug. "),
            Updates.Text("Applying a minimal edit."),
            Updates.Call("call_edit_1", "edit", """{"path":"src/Calculator.cs","old_text":"return a - b;","new_text":"return a + b;"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Done: read the file, fixed the operator, and applied the edit."),
            Updates.Usage(1834, 41),
            Updates.Finish(),
        ]),
    ];

    public static IReadOnlyList<ScriptedTurn> SplitArguments() =>
    [
        new(
        [
            Updates.Text("Reading the file; arguments may arrive in pieces."),
            .. Updates.SplitCall("call_read_split", "read", """{"pa""", """th":"src/Ca""", """lculator.cs"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Applying the edit."),
            .. Updates.SplitCall("call_edit_split", "edit", """{"path":"src/Calculator.cs","old_text":"return a -""", """ b;","new_text":"return a + b;"}"""),
            Updates.Finish(ChatFinishReason.ToolCalls),
        ]),
        new(
        [
            Updates.Text("Fixed after assembling split argument chunks."),
            Updates.Finish(),
        ]),
    ];
}
