using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Tests;

public sealed class StreamAccumulatorTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_assembles_fragmented_arguments_into_one_complete_call()
    {
        var provider = new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Checking. ")]) { ModelId = "claude-sonnet-5-5" },
                FragmentUpdate("call-1", "list_files", "{\"path\":"),
                FragmentUpdate("call-1", string.Empty, " \"a.txt\"}"),
                new ChatResponseUpdate(ChatRole.Assistant, []) { ModelId = "claude-sonnet-5-5", FinishReason = ChatFinishReason.ToolCalls });
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        Assert.Equal("Checking. ", string.Concat(updates.Select(update => update.Text)));
        FunctionCallContent call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("list_files", call.Name);
        Assert.Equal("a.txt", Unwrap(call.Arguments!["path"]));
        Assert.Contains(updates, update => update.FinishReason == ChatFinishReason.ToolCalls);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_passes_already_complete_calls_through_unchanged()
    {
        var complete = new ChatResponseUpdate(
            ChatRole.Assistant,
            [new FunctionCallContent("call-1", "list_files", new Dictionary<string, object?> { ["path"] = "a.txt" })])
        {
            ModelId = "gpt-4o-mini",
        };
        var provider = new ScriptedChatClient().Enqueue(complete);
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        Assert.Same(complete, Assert.Single(updates));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_merges_multiple_concurrent_calls_independently()
    {
        var provider = new ScriptedChatClient()
            .Enqueue(
                FragmentUpdate("call-a", "read", "{\"path\":\"a"),
                FragmentUpdate("call-b", "write", "{\"path\":\"b"),
                FragmentUpdate("call-a", string.Empty, ".txt\"}"),
                FragmentUpdate("call-b", string.Empty, ".txt\"}"));
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        FunctionCallContent[] calls = [.. updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>()];
        Assert.Equal(2, calls.Length);
        Assert.Equal("call-a", calls[0].CallId);
        Assert.Equal("read", calls[0].Name);
        Assert.Equal("a.txt", Unwrap(calls[0].Arguments!["path"]));
        Assert.Equal("call-b", calls[1].CallId);
        Assert.Equal("write", calls[1].Name);
        Assert.Equal("b.txt", Unwrap(calls[1].Arguments!["path"]));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_keeps_interleaved_text_updates_in_order()
    {
        var provider = new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("first ")]) { ModelId = "gpt-4o-mini" },
                FragmentUpdate("call-1", "list_files", "{\"path\":\"a"),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("second ")]) { ModelId = "gpt-4o-mini" },
                FragmentUpdate("call-1", string.Empty, ".txt\"}"),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("third")]) { ModelId = "gpt-4o-mini" });
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        string text = string.Concat(updates.Select(update => update.Text));
        Assert.Equal("first second third", text);
        Assert.DoesNotContain(
            updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>(),
            call => call.Arguments?.ContainsKey(StreamAccumulator.ArgumentsFragmentKey) == true);
        FunctionCallContent call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("a.txt", Unwrap(call.Arguments!["path"]));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_carries_update_metadata_through_fragment_updates()
    {
        ResponseContinuationToken token = ResponseContinuationToken.FromBytes("resume-1"u8.ToArray());
        var fragmentUpdate = new ChatResponseUpdate(ChatRole.Assistant, [Fragment("call-1", "list_files", "{\"path\":\"a")])
        {
            ModelId = "gpt-4o-mini",
            ResponseId = "resp-1",
            MessageId = "msg-1",
            ContinuationToken = token,
        };
        var provider = new ScriptedChatClient()
            .Enqueue(fragmentUpdate, FragmentUpdate("call-1", string.Empty, ".txt\"}"));
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        Assert.Equal("resp-1", updates[0].ResponseId);
        Assert.Equal("msg-1", updates[0].MessageId);
        Assert.Equal(token.ToBytes().ToArray(), updates[0].ContinuationToken!.ToBytes().ToArray());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_preserves_finish_reason_on_a_fragment_only_update()
    {
        var provider = new ScriptedChatClient()
            .Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [Fragment("call-1", "list_files", "{\"path\":\"a.txt\"}")])
            {
                ModelId = "gpt-4o-mini",
                FinishReason = ChatFinishReason.ToolCalls,
            });
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        ChatResponseUpdate fragmentUpdate = updates[0];
        Assert.Empty(fragmentUpdate.Contents);
        Assert.Equal(ChatFinishReason.ToolCalls, fragmentUpdate.FinishReason);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_copies_update_fields_when_stripping_fragments()
    {
        DateTimeOffset createdAt = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        object rawRepresentation = new();
        var provider = new ScriptedChatClient()
            .Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [Fragment("call-1", "list_files", "{\"path\":\"a.txt\"}")])
            {
                ModelId = "gpt-4o-mini",
                AuthorName = "assistant-author",
                ConversationId = "conversation-1",
                CreatedAt = createdAt,
                AdditionalProperties = new AdditionalPropertiesDictionary { ["trace"] = "abc" },
                RawRepresentation = rawRepresentation,
            });
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        ChatResponseUpdate stripped = updates[0];
        Assert.Empty(stripped.Contents);
        Assert.Equal("assistant-author", stripped.AuthorName);
        Assert.Equal("conversation-1", stripped.ConversationId);
        Assert.Equal(createdAt, stripped.CreatedAt);
        Assert.Equal("abc", stripped.AdditionalProperties!["trace"]);
        Assert.Same(rawRepresentation, stripped.RawRepresentation);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_surfaces_unparseable_assembled_arguments_as_an_exception()
    {
        var provider = new ScriptedChatClient()
            .Enqueue(
                FragmentUpdate("call-1", "list_files", "{\"path\":"),
                FragmentUpdate("call-1", string.Empty, " broken"));
        var accumulator = new StreamAccumulator(provider);

        List<ChatResponseUpdate> updates = await Stream(accumulator);

        FunctionCallContent call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.NotNull(call.Exception);
        Assert.Null(call.Arguments);
    }

    private static ChatResponseUpdate FragmentUpdate(string callId, string name, string json) =>
        new(ChatRole.Assistant, [Fragment(callId, name, json)]) { ModelId = "gpt-4o-mini" };

    private static FunctionCallContent Fragment(string callId, string name, string json) =>
        new(callId, name, new Dictionary<string, object?> { [StreamAccumulator.ArgumentsFragmentKey] = json });

    private static object? Unwrap(object? value) => value is JsonElement element ? element.GetString() : value;

    private static async Task<List<ChatResponseUpdate>> Stream(IChatClient client) =>
        await client
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
}
