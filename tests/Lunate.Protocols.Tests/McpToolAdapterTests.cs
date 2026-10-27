using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lunate.Agent;
using Lunate.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols.Tests;

public sealed class McpToolAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_round_trip_returns_text_and_structured_content()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );
        var add = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__add");

        var result = await add.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { a = 2, b = 3 }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(result.IsError);
        Assert.Contains("5", result.Output, StringComparison.Ordinal);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(5, result.StructuredContent.Value.GetProperty("sum").GetInt32());
    }

    [Fact]
    public async Task Boom_tool_returns_an_error_result_and_the_client_stays_usable()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );
        var tools = await host.GetToolsAsync(Ct);
        var boom = tools.Single(tool => tool.Name == "server__boom");

        var result = await boom.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);

        var echo = tools.Single(tool => tool.Name == "server__echo");
        var echoResult = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "still here" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(echoResult.IsError);
        Assert.Equal("still here", echoResult.Output);
    }

    [Fact]
    public async Task Slow_call_cancelled_with_the_turn_and_the_server_observes_it()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var log = new ConcurrentQueue<string>();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(marker),
            log: log.Enqueue
        );
        var slow = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__slow");

        using var cancellation = new CancellationTokenSource();
        var call = slow.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { seconds = 30 }),
            McpTestEnvironment.Context,
            cancellation.Token
        );

        await TestMcpServer.WaitForMarkerLineAsync(
            marker,
            "slow_started",
            TimeSpan.FromSeconds(15)
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await AssertCancellationObservedAsync(marker, log);
        // A cancellation that lands is not news: nothing is reported for it.
        Assert.Empty(log);
    }

    [Fact]
    public async Task Cancellation_notice_that_cannot_be_written_is_reported()
    {
        using var temp = new TempDirectory();
        var log = new ConcurrentQueue<string>();
        var options = McpTestEnvironment.Options(temp.File("marker.txt"));

        // A real client over a transport that refuses the cancel notification: the write fails the
        // way a closed stdio pipe does, on every platform and without timing or file-system
        // dependencies. Disposing the host did not do this — on Windows the pipe still accepted the
        // write, so the case never happened there.
        var transport = new RefuseCancelledNoticesTransport(
            new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name = options.Name,
                    Command = options.Command,
                    Arguments = [.. options.Arguments],
                    EnvironmentVariables = options.Environment?.ToDictionary(
                        pair => pair.Key,
                        pair => (string?)pair.Value
                    ),
                }
            )
        );
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "lunate-tests", Version = "1.0.0" },
                ProtocolVersion = "2025-11-25",
            },
            loggerFactory: null,
            cancellationToken: Ct
        );
        var slow = (await client.ListToolsAsync(cancellationToken: Ct)).Single(tool =>
            tool.Name == "slow"
        );
        var adapter = new McpToolAdapter(
            client,
            "server",
            "server",
            ToolExposure.Direct,
            slow,
            TimeSpan.FromSeconds(60),
            log.Enqueue
        );

        // The server is gone by the time the turn is cancelled: nobody is left to notify, and the
        // call it is still running has nobody waiting for it — the case that must not be silent.
        var sent = await adapter
            .NotifyCancellationAsync(new RequestId("gone"))
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(sent);
        Assert.Contains(
            log,
            line => line.Contains("notifications/cancelled", StringComparison.Ordinal)
        );
    }

    /// <summary>
    /// Refuses <c>notifications/cancelled</c> at the transport and passes everything else through,
    /// which is what a torn-down connection does: the client is connected, and the one write that
    /// matters fails.
    /// </summary>
    private sealed class RefuseCancelledNoticesTransport(IClientTransport inner) : IClientTransport
    {
        public string Name => inner.Name;

        public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken) =>
            new RefuseCancelledNoticesSession(
                await inner.ConnectAsync(cancellationToken).ConfigureAwait(false)
            );

        private sealed class RefuseCancelledNoticesSession(ITransport inner) : ITransport
        {
            public string? SessionId => inner.SessionId;

            public ChannelReader<JsonRpcMessage> MessageReader => inner.MessageReader;

            public Task SendMessageAsync(
                JsonRpcMessage message,
                CancellationToken cancellationToken
            ) =>
                message is JsonRpcNotification { Method: NotificationMethods.CancelledNotification }
                    ? Task.FromException(new IOException("The transport is closed."))
                    : inner.SendMessageAsync(message, cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    /// <summary>
    /// Both legs of the cancellation, checked separately: <c>cancel_notice</c> proves the
    /// notification reached the server, <c>slow_cancelled</c> proves the server then stopped the
    /// call. A timeout names the leg that lost the message, with whatever the client logged about
    /// the write — a server that never answers must not read as an unexplained 15 s wait.
    /// </summary>
    private static async Task AssertCancellationObservedAsync(
        string marker,
        ConcurrentQueue<string> log
    )
    {
        try
        {
            await TestMcpServer.WaitForMarkerLineAsync(
                marker,
                "cancel_notice",
                TimeSpan.FromSeconds(15)
            );
            await TestMcpServer.WaitForMarkerLineAsync(
                marker,
                "slow_cancelled",
                TimeSpan.FromSeconds(15)
            );
        }
        catch (TimeoutException exception)
        {
            Assert.Fail(
                $"{exception.Message} | client log: {(log.IsEmpty ? "<none>" : string.Join(" | ", log))}"
            );
        }
    }

    [Fact]
    public async Task Slow_call_times_out_as_an_error_result_and_the_client_stays_usable()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(
                temp.File("marker.txt"),
                callTimeout: TimeSpan.FromMilliseconds(200)
            )
        );
        var tools = await host.GetToolsAsync(Ct);
        var slow = tools.Single(tool => tool.Name == "server__slow");

        var result = await slow.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { seconds = 30 }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains("timed out", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);

        var echo = tools.Single(tool => tool.Name == "server__echo");
        var echoResult = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "still here" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(echoResult.IsError);
        Assert.Equal("still here", echoResult.Output);
    }

    [Fact]
    public void Non_object_result_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult("server", "tool", JsonNode.Parse("42"));

        AssertMalformed(result);
    }

    [Fact]
    public void Wrong_typed_content_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult(
            "server",
            "tool",
            JsonNode.Parse("""{"content":"not-a-list"}""")
        );

        AssertMalformed(result);
    }

    [Fact]
    public void Non_object_structured_content_becomes_a_malformed_error_result()
    {
        var result = McpToolAdapter.MapCallResult(
            "server",
            "tool",
            JsonNode.Parse("""{"structuredContent":42}""")
        );

        AssertMalformed(result);
    }

    private static void AssertMalformed(ToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);
        Assert.Contains("tool", result.Output, StringComparison.Ordinal);
        Assert.Contains("malformed", result.Output, StringComparison.OrdinalIgnoreCase);
    }
}
