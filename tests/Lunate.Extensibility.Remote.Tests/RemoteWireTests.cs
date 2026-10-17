using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The wire's framing and correlation: round-trips, separate id spaces per direction, concurrent
/// calls, cancellation both ways, error mapping, timeouts and every protocol-error rule.
/// </summary>
public sealed class RemoteWireTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calls_round_trip_between_two_ends()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        child.RequestReceived += (method, parameters, _) =>
        {
            Assert.Equal("hook/ToolCalling", method);
            Assert.Equal("read", parameters!.Value.GetProperty("tool").GetString());
            using var document = JsonDocument.Parse("""{"outcome":"proceed"}""");
            return ValueTask.FromResult<JsonElement?>(document.RootElement.Clone());
        };
        host.Start();
        child.Start();

        JsonElement? result = await host.CallAsync(
            "hook/ToolCalling",
            JsonDocument.Parse("""{"tool":"read"}""").RootElement.Clone(),
            cancellationToken: Ct
        );

        Assert.Equal("proceed", result!.Value.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Separate_id_spaces_per_direction_do_not_collide()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        host.RequestReceived += (_, _, _) => Echo("from-host");
        child.RequestReceived += (_, _, _) => Echo("from-child");
        host.Start();
        child.Start();

        // Both ends issue id 1 concurrently; the frame type (method vs. response) keeps them apart.
        Task<JsonElement?> hostCall = host.CallAsync("call/A", cancellationToken: Ct);
        Task<JsonElement?> childCall = child.CallAsync("call/B", cancellationToken: Ct);

        Assert.Equal("from-child", (await hostCall)!.Value.GetString());
        Assert.Equal("from-host", (await childCall)!.Value.GetString());
    }

    [Fact]
    public async Task Concurrent_requests_correlate()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        child.RequestReceived += async (method, parameters, ct) =>
        {
            int index = parameters!.Value.GetInt32();
            await Task.Delay(index % 5 * 10, ct);
            using var document = JsonDocument.Parse(
                $$"""{"index":{{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""
            );
            return document.RootElement.Clone();
        };
        host.Start();
        child.Start();

        Task<JsonElement?>[] calls =
        [
            .. Enumerable
                .Range(0, 25)
                .Select(index =>
                    host.CallAsync(
                        "call",
                        JsonDocument
                            .Parse(
                                $$"""{{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}}"""
                            )
                            .RootElement.Clone(),
                        cancellationToken: Ct
                    )
                ),
        ];

        JsonElement?[] results = await Task.WhenAll(calls);
        Assert.Equal(
            Enumerable.Range(0, 25),
            results.Select(result => result!.Value.GetProperty("index").GetInt32())
        );
    }

    [Fact]
    public async Task A_call_timeout_sends_cancel_and_throws()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        var canceled = new TaskCompletionSource();
        child.RequestReceived += async (method, _, ct) =>
        {
            if (method == "slow")
            {
                using var registration = ct.Register(canceled.SetResult);
                await Task.Delay(Timeout.Infinite, ct);
            }

            return null;
        };
        host.Start();
        child.Start();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            host.CallAsync("slow", timeout: TimeSpan.FromMilliseconds(50), cancellationToken: Ct)
        );

        // The cancel reached its target: the child's request token fired. And the wire survived
        // it — a cancel that threw on the receiving side would have closed the connection — so a
        // following call still round-trips.
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        await host.CallAsync("fast", cancellationToken: Ct);
    }

    [Fact]
    public async Task A_malformed_cancel_request_is_ignored_and_the_wire_survives()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.Start();

        // A notification-form cancel with no params at all, and one naming an unknown id: both
        // are no-ops — never an exception, never a closed wire.
        raw.Write("""{"jsonrpc":"2.0","method":"$/cancelRequest"}""");
        raw.Write("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":9999}}""");

        // The wire is still healthy: a request round-trips.
        var received = new TaskCompletionSource();
        host.RequestReceived += (_, _, _) =>
        {
            received.TrySetResult();
            return ValueTask.FromResult<JsonElement?>(null);
        };
        raw.Write("""{"jsonrpc":"2.0","id":7,"method":"probe"}""");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        string response = await raw.WaitFor(line =>
            line.Contains("\"id\":7", StringComparison.Ordinal)
        );
        Assert.Contains("result", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_sends_cancel_and_propagates()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.Start();

        using var cts = new CancellationTokenSource();
        Task<JsonElement?> call = host.CallAsync("slow", cancellationToken: cts.Token);
        JsonNode request = JsonNode.Parse(
            await raw.WaitFor(line =>
                line.Contains("\"method\":\"slow\"", StringComparison.Ordinal)
            )
        )!;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        string cancel = await raw.WaitFor(line =>
            line.Contains("$/cancelRequest", StringComparison.Ordinal)
        );
        JsonNode frame = JsonNode.Parse(cancel)!;
        Assert.Equal(request["id"]!.ToJsonString(), frame["params"]!["id"]!.ToJsonString());
    }

    [Fact]
    public async Task A_peer_cancel_notification_cancels_the_incoming_token_and_answers_cancelled()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        var handlerStarted = new TaskCompletionSource();
        var tokenFired = new TaskCompletionSource();
        host.RequestReceived += async (_, _, ct) =>
        {
            handlerStarted.TrySetResult();
            using var registration = ct.Register(tokenFired.SetResult);
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        host.Start();

        raw.Write(
            """{"jsonrpc":"2.0","id":7,"method":"service/memory/store","params":{"text":"x"}}"""
        );
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        raw.Write("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":7}}""");
        await tokenFired.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        string response = await raw.WaitFor(line =>
            line.Contains("\"id\":7", StringComparison.Ordinal)
        );
        JsonNode frame = JsonNode.Parse(response)!;
        Assert.Equal(-32800, frame["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task An_error_response_maps_to_a_RemoteCallException()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.Start();

        Task<JsonElement?> call = host.CallAsync("initialize", cancellationToken: Ct);
        string request = await raw.WaitFor(line =>
            line.Contains("initialize", StringComparison.Ordinal)
        );
        JsonNode frame = JsonNode.Parse(request)!;
        raw.Write(
            "{\"jsonrpc\":\"2.0\",\"id\":"
                + frame["id"]!.ToJsonString()
                + ",\"error\":{\"code\":-32601,\"message\":\"unknown method\"}}"
        );

        RemoteCallException exception = await Assert.ThrowsAsync<RemoteCallException>(() => call);
        Assert.Equal(-32601, exception.Code);
        Assert.Equal("unknown method", exception.PeerMessage);
    }

    [Fact]
    public async Task A_handler_exception_becomes_an_error_result()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.RequestReceived += (_, _, _) => throw new InvalidOperationException("boom");
        host.Start();

        raw.Write("""{"jsonrpc":"2.0","id":3,"method":"service/x/y"}""");

        string response = await raw.WaitFor(line =>
            line.Contains("\"id\":3", StringComparison.Ordinal)
        );
        JsonNode frame = JsonNode.Parse(response)!;
        Assert.Contains(
            "boom",
            frame["error"]!["message"]!.GetValue<string>(),
            StringComparison.Ordinal
        );
        Assert.Equal(-32603, frame["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_non_json_line_is_a_protocol_error_and_fails_in_flight_calls()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        var failed = new TaskCompletionSource<RemoteProtocolException>();
        host.ProtocolFailed += exception => failed.TrySetResult(exception);
        host.Start();

        Task<JsonElement?> call = host.CallAsync("initialize", cancellationToken: Ct);
        await raw.WaitFor(line => line.Contains("initialize", StringComparison.Ordinal));
        raw.Write("this is not json");

        RemoteProtocolException protocol = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Contains("non-JSON", protocol.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<RemoteChannelClosedException>(() => call);
    }

    [Fact]
    public async Task A_batch_frame_is_refused()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        var failed = new TaskCompletionSource<RemoteProtocolException>();
        host.ProtocolFailed += exception => failed.TrySetResult(exception);
        host.Start();

        raw.Write("""[{"jsonrpc":"2.0","id":1,"method":"x"}]""");

        RemoteProtocolException protocol = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Contains("batch", protocol.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_frame_without_jsonrpc_2_is_refused()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        var failed = new TaskCompletionSource<RemoteProtocolException>();
        host.ProtocolFailed += exception => failed.TrySetResult(exception);
        host.Start();

        raw.Write("""{"id":1,"method":"x"}""");

        RemoteProtocolException protocol = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Contains("jsonrpc", protocol.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_with_unknown_id_is_ignored()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.Start();

        raw.Write("""{"jsonrpc":"2.0","id":999,"result":{}}""");
        raw.Write("""{"jsonrpc":"2.0","id":1000,"method":"ping"}""");

        string response = await raw.WaitFor(line =>
            line.Contains("\"id\":1000", StringComparison.Ordinal)
        );
        JsonNode frame = JsonNode.Parse(response)!;
        Assert.Equal(-32601, frame["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Outgoing_frames_are_compact_no_bom_one_object_per_line()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a);
        host.Start();

        _ = host.CallAsync(
            "x",
            JsonDocument.Parse("""{"a":"b"}""").RootElement.Clone(),
            cancellationToken: Ct
        );
        string line = await raw.WaitFor(_ => true);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\uFEFF', line);
        Assert.StartsWith("{\"jsonrpc\":\"2.0\",", line, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal("x", document.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task An_oversized_line_is_a_protocol_error()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        var raw = new RawPeer(b);
        await using var host = new RemoteWire(a, new RemoteWireOptions { MaxLineBytes = 128 });
        var failed = new TaskCompletionSource<RemoteProtocolException>();
        host.ProtocolFailed += exception => failed.TrySetResult(exception);
        host.Start();

        raw.Write("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + new string('x', 200) + "\"}");

        RemoteProtocolException protocol = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Contains("over 128 bytes", protocol.Message, StringComparison.Ordinal);
    }

    private static ValueTask<JsonElement?> Echo(string text)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(text));
        return ValueTask.FromResult<JsonElement?>(document.RootElement.Clone());
    }

    /// <summary>A raw frame peer that reads frames into a list and lets tests await a predicate.</summary>
    private sealed class RawPeer(IRemoteLineChannel channel)
    {
        private readonly List<string> _lines = [];
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private Task? _loop;

        public void Write(string line) =>
            _ = Task.Run(() => channel.WriteLineAsync(line, CancellationToken.None).AsTask());

        public async Task<string> WaitFor(Func<string, bool> predicate)
        {
            await EnsureLoopAsync().ConfigureAwait(false);
            for (var attempt = 0; attempt < 300; attempt++)
            {
                lock (_lines)
                {
                    string? match = _lines.FirstOrDefault(predicate);
                    if (match is not null)
                    {
                        return match;
                    }
                }

                await Task.Delay(20, Ct).ConfigureAwait(false);
            }

            throw new TimeoutException("no matching frame arrived within 6 seconds");
        }

        private async Task EnsureLoopAsync()
        {
            await _gate.WaitAsync(Ct).ConfigureAwait(false);
            try
            {
                if (_loop is not null)
                {
                    return;
                }

                _loop = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            string? line = await channel
                                .ReadLineAsync(_lifetime.Token)
                                .ConfigureAwait(false);
                            if (line is null)
                            {
                                return;
                            }

                            lock (_lines)
                            {
                                _lines.Add(line);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                });
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
