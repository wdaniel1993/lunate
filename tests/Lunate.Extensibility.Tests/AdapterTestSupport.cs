using System.Runtime.CompilerServices;
using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Tests;

internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<List<ChatResponseUpdate>> _responses = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions?> RequestOptions { get; } = [];

    public FakeChatClient Enqueue(params ChatResponseUpdate[] updates)
    {
        _responses.Enqueue([.. updates]);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Dequeue(messages, options).ToChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (ChatResponseUpdate update in Dequeue(messages, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private List<ChatResponseUpdate> Dequeue(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options
    )
    {
        Requests.Add([.. messages]);
        RequestOptions.Add(options);
        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("FakeChatClient has no scripted response left.");
    }
}

internal sealed class FakeTool(string name, string output) : ITool
{
    public string Name { get; } = name;

    public string Description { get; } = name;

    public JsonElement ParametersSchema { get; } =
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    public ToolAnnotations? Annotations { get; init; }

    public string? ReceivedArgs { get; private set; }

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        ReceivedArgs = args.GetRawText();
        return Task.FromResult(new ToolResult(output, IsError: false));
    }
}

internal static class AdapterTestSupport
{
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static string Text(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    public static async Task<List<AgentEvent>> Run(AgentHarness harness, CancellationToken ct)
    {
        List<AgentEvent> events = [];
        await foreach (AgentEvent agentEvent in harness.RunAsync("go", ct))
        {
            events.Add(agentEvent);
        }

        return events;
    }
}
