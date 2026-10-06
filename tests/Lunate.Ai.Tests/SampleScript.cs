using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

internal static class SampleScript
{
    internal const string ModelId = "gpt-4o-mini";
    internal const string FileName = "scripted-chat.jsonl";

    internal static IReadOnlyList<SampleExchange> Exchanges { get; } =
    [
        new SampleExchange(
            [new ChatMessage(ChatRole.User, "List the files in the workspace.")],
            Options(),
            [
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new TextContent("I will check the workspace.")]
                )
                {
                    ModelId = ModelId,
                },
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-1",
                            "list_files",
                            new Dictionary<string, object?>()
                        ),
                    ]
                )
                {
                    ModelId = ModelId,
                },
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    ModelId = ModelId,
                    FinishReason = ChatFinishReason.ToolCalls,
                },
            ]
        ),
        new SampleExchange(
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "a.txt\nb.txt")])],
            Options(),
            [
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new TextContent("There are two files: a.txt and b.txt.")]
                )
                {
                    ModelId = ModelId,
                },
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new UsageContent(
                            new UsageDetails { InputTokenCount = 42, OutputTokenCount = 17 }
                        ),
                    ]
                )
                {
                    ModelId = ModelId,
                },
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    ModelId = ModelId,
                    FinishReason = ChatFinishReason.Stop,
                },
            ]
        ),
    ];

    internal static ChatOptions Options() =>
        new() { ModelId = ModelId, Tools = [new NamedTool("list_files")] };

    internal sealed record SampleExchange(
        ChatMessage[] Messages,
        ChatOptions Options,
        ChatResponseUpdate[] Updates
    );
}
