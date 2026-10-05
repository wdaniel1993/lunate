using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public sealed class RecordingChatClient : DelegatingChatClient
{
    private readonly string _fixturePath;
    private readonly string? _modelId;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _writeLock = new();

    public RecordingChatClient(IChatClient innerClient, string fixturePath, string? modelId = null)
        : this(innerClient, fixturePath, modelId, TimeProvider.System)
    {
    }

    internal RecordingChatClient(IChatClient innerClient, string fixturePath, string? modelId, TimeProvider timeProvider)
        : base(innerClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        _fixturePath = fixturePath;
        _modelId = modelId;
        _timeProvider = timeProvider;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessage> request = Materialize(messages);
        string requestDigest = FixtureFormat.ComputeRequestDigest(request, options);
        var recorded = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(request, options, cancellationToken))
        {
            recorded.Add(update);
            yield return update;
        }

        AppendExchange(requestDigest, recorded);
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessage> request = Materialize(messages);
        string requestDigest = FixtureFormat.ComputeRequestDigest(request, options);
        ChatResponse response = await base.GetResponseAsync(request, options, cancellationToken);
        AppendExchange(requestDigest, response.ToChatResponseUpdates());
        return response;
    }

    private static IReadOnlyList<ChatMessage> Materialize(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages as IReadOnlyList<ChatMessage> ?? [.. messages];
    }

    private void AppendExchange(string requestDigest, IReadOnlyList<ChatResponseUpdate> updates)
    {
        string line = FixtureFormat.SerializeExchange(requestDigest, updates);
        lock (_writeLock)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(_fixturePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(_fixturePath, append: true);
            if (writer.BaseStream.Length == 0)
            {
                writer.WriteLine(FixtureFormat.SerializeHeader(_modelId ?? "unknown", _timeProvider.GetUtcNow()));
            }

            writer.WriteLine(line);
        }
    }
}
