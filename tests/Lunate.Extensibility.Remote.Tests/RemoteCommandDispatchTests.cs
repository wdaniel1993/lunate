using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The child-side entry for <c>command/invoke</c>: the request id is the key the wire's
/// incoming-request map cancels, so the handler's token follows <c>$/cancelRequest</c> and only
/// the named invocation; a throwing handler answers a <c>failed</c> reply carrying its message,
/// an error result answers <c>failed</c>, and a completed handler answers its output unchanged.
/// </summary>
public sealed class RemoteCommandDispatchTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_completed_handler_answers_completed_with_its_output()
    {
        JsonElement notice = await InvokeAsync(
            "ping",
            "hello",
            (_, argument, _) => ValueTask.FromResult(CommandResult.Notice("pong " + argument))
        );
        Assert.Equal("completed", RemoteCommandWireTests.Disposition(notice));
        Assert.Equal("pong hello", notice.GetProperty("output").GetString());
        Assert.Equal(JsonValueKind.Null, notice.GetProperty("error").ValueKind);

        JsonElement silent = await InvokeAsync(
            "quiet",
            string.Empty,
            (_, _, _) => ValueTask.FromResult(CommandResult.None)
        );
        Assert.Equal("completed", RemoteCommandWireTests.Disposition(silent));
        Assert.Equal(JsonValueKind.Null, silent.GetProperty("output").ValueKind);
    }

    [Fact]
    public async Task A_throwing_handler_answers_failed_carrying_its_message()
    {
        JsonElement reply = await InvokeAsync(
            "explode",
            string.Empty,
            (_, _, _) => throw new InvalidOperationException("the extension cannot ping right now")
        );

        Assert.Equal("failed", RemoteCommandWireTests.Disposition(reply));
        Assert.Equal("the extension cannot ping right now", reply.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, reply.GetProperty("output").ValueKind);
    }

    [Fact]
    public async Task An_error_result_answers_failed_with_the_same_message()
    {
        JsonElement reply = await InvokeAsync(
            "refuse",
            string.Empty,
            (_, _, _) => ValueTask.FromResult(CommandResult.Error("no arguments were given"))
        );

        Assert.Equal("failed", RemoteCommandWireTests.Disposition(reply));
        Assert.Equal("no arguments were given", reply.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cancelling_one_invoke_leaves_the_other_running()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.RequestReceived += (_, parameters, cancellationToken) =>
            RemoteCommandInvoke.DispatchAsync(
                RemoteCommandWireTests.Deserialize<CommandInvokeRequest>(parameters!.Value),
                async (name, _, token) =>
                {
                    if (name == "wait")
                    {
                        waiting.TrySetResult();
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }

                    // The other invocation proves the cancel named one id, not the extension.
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return CommandResult.Notice("pong");
                },
                cancellationToken
            );
        host.Start();
        child.Start();

        long waitingId = 0;
        Task<JsonElement?> blocked = host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id =>
            {
                waitingId = id;
                return RemoteCommandWireTests.Serialize(
                    new CommandInvokeRequest("wait", string.Empty, id)
                );
            },
            cancellationToken: CancellationToken.None
        );
        Task<JsonElement?> other = host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id =>
                RemoteCommandWireTests.Serialize(
                    new CommandInvokeRequest("ping", string.Empty, id)
                ),
            cancellationToken: CancellationToken.None
        );
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await host.NotifyAsync(
            RemoteProtocol.CancelMethod,
            RemoteCommandWireTests.Cancel(waitingId),
            Ct
        );

        JsonElement? cancelled = await blocked.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("cancelled", RemoteCommandWireTests.Disposition(cancelled!.Value));

        release.TrySetResult();
        JsonElement? completed = await other.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("completed", RemoteCommandWireTests.Disposition(completed!.Value));
        Assert.Equal("pong", completed.Value.GetProperty("output").GetString());
    }

    /// <summary>Runs one invocation over a real wire pair and returns the child's reply frame.</summary>
    private static async Task<JsonElement> InvokeAsync(
        string name,
        string argument,
        RemoteCommandHandler handler
    )
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        child.RequestReceived += (_, parameters, cancellationToken) =>
            RemoteCommandInvoke.DispatchAsync(
                RemoteCommandWireTests.Deserialize<CommandInvokeRequest>(parameters!.Value),
                handler,
                cancellationToken
            );
        host.Start();
        child.Start();

        JsonElement? reply = await host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id => RemoteCommandWireTests.Serialize(new CommandInvokeRequest(name, argument, id)),
            cancellationToken: Ct
        );
        Assert.NotNull(reply);
        return reply!.Value;
    }
}
