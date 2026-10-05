using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Spike.Shared;

public sealed class RecordingChatClient(IChatClient inner, string path)
    : DelegatingChatClient(inner)
{
    private static readonly JsonSerializerOptions JsonlOptions = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };

    private bool _headerWritten;
    private int _turnIndex;

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (!_headerWritten)
        {
            File.Delete(path);
            await File.AppendAllTextAsync(
                path,
                """{"type":"header","schema":1,"model":"spike-stub","created":"2026-10-04T00:00:00Z"}"""
                    + "\n",
                cancellationToken
            );
            _headerWritten = true;
        }

        await File.AppendAllTextAsync(
            path,
            $$"""{"type":"turn","index":{{_turnIndex++}}}""" + "\n",
            cancellationToken
        );

        await foreach (
            ChatResponseUpdate update in base.GetStreamingResponseAsync(
                messages,
                options,
                cancellationToken
            )
        )
        {
            await File.AppendAllTextAsync(
                path,
                JsonSerializer.Serialize(update, JsonlOptions) + "\n",
                cancellationToken
            );
            yield return update;
        }
    }
}

public sealed class ReplayChatClient(string path) : IChatClient
{
    private static readonly JsonSerializerOptions JsonlOptions = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };

    private List<List<ChatResponseUpdate>>? _turns;
    private int _nextTurn;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("The spike replay client is streaming-only.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        _ = messages;
        _ = options;

        this._turns ??= await ReadTurnsAsync(path, cancellationToken);
        if (this._nextTurn >= this._turns.Count)
        {
            yield break;
        }

        foreach (ChatResponseUpdate update in this._turns[this._nextTurn++])
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private static async Task<List<List<ChatResponseUpdate>>> ReadTurnsAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        List<List<ChatResponseUpdate>> turns = [];

        foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("type", out JsonElement type))
            {
                switch (type.GetString())
                {
                    case "header":
                        continue;
                    case "turn":
                        turns.Add([]);
                        continue;
                }
            }

            ChatResponseUpdate update =
                JsonSerializer.Deserialize<ChatResponseUpdate>(line, JsonlOptions)
                ?? throw new InvalidOperationException($"Could not deserialize update: {line}");
            if (turns.Count == 0)
            {
                turns.Add([]);
            }

            turns[^1].Add(update);
        }

        return turns;
    }
}
