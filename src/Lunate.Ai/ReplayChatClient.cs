using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public sealed class ReplayChatClient : IChatClient
{
    private const int DigestDisplayLength = 12;

    private readonly string _fixturePath;
    private readonly FixtureDocument _document;
    private int _nextExchange;

    public ReplayChatClient(string fixturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        _fixturePath = fixturePath;
        _document = FixtureFormat.ReadFile(fixturePath);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        FixtureExchange exchange = NextExchange(messages, options);
        await Task.CompletedTask;
        foreach (ChatResponseUpdate update in exchange.Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        FixtureExchange exchange = NextExchange(messages, options);
        await Task.CompletedTask;
        return exchange.Updates.ToChatResponse();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private FixtureExchange NextExchange(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (_nextExchange >= _document.Exchanges.Count)
        {
            throw new InvalidOperationException(
                $"Fixture '{_fixturePath}' is exhausted: it has {_document.Exchanges.Count} exchange(s) and request {_nextExchange + 1} arrived. Re-record the fixture with LUNATE_RECORD=1 if the request sequence changed."
            );
        }

        FixtureExchange exchange = _document.Exchanges[_nextExchange];
        string actualDigest = FixtureFormat.ComputeRequestDigest(messages, options);
        if (!string.Equals(exchange.RequestDigest, actualDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Fixture '{_fixturePath}' has a request mismatch at exchange {_nextExchange + 1}: expected digest '{Display(exchange.RequestDigest)}' but got '{Display(actualDigest)}'. The request differs from the recorded one; re-record the fixture with LUNATE_RECORD=1 if that change is intentional."
            );
        }

        _nextExchange++;
        return exchange;
    }

    private static string Display(string digest) =>
        digest.Length <= DigestDisplayLength ? digest : digest[..DigestDisplayLength];
}
