using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// The command-invocation wire (protocol version 2): the request carries the bare command name,
/// the argument remainder and the request id it was framed under; the reply mirrors the
/// in-process result's output, failure message and disposition; and cancellation rides the
/// existing <c>$/cancelRequest</c> with that id — a cancel for a finished invocation is a no-op.
/// </summary>
public sealed class RemoteCommandWireTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_invoke_request_carries_the_bare_name_the_remainder_and_its_request_id()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        CommandInvokeRequest? seen = null;
        long framedUnder = 0;
        child.RequestReceived += (method, parameters, cancellationToken) =>
        {
            Assert.Equal("command/invoke", method);
            seen = Deserialize<CommandInvokeRequest>(parameters!.Value);
            return RemoteCommandInvoke.DispatchAsync(
                seen,
                (_, argument, _) => ValueTask.FromResult(CommandResult.Notice("pong " + argument)),
                cancellationToken
            );
        };
        host.Start();
        child.Start();

        JsonElement? reply = await host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id =>
            {
                framedUnder = id;
                return Serialize(new CommandInvokeRequest("ping", "hello world", id));
            },
            cancellationToken: Ct
        );

        Assert.NotNull(seen);
        Assert.Equal("ping", seen!.Name);
        Assert.Equal("hello world", seen.Argument);
        Assert.Equal(framedUnder, seen.RequestId);
        Assert.Equal("completed", Disposition(reply!.Value));
        Assert.Equal("pong hello world", reply.Value.GetProperty("output").GetString());
        Assert.Equal(JsonValueKind.Null, reply.Value.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task A_cancel_with_the_invoke_id_reaches_the_handler_and_the_reply_is_cancelled()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.RequestReceived += (_, parameters, cancellationToken) =>
            RemoteCommandInvoke.DispatchAsync(
                Deserialize<CommandInvokeRequest>(parameters!.Value),
                async (_, _, token) =>
                {
                    started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        observed.TrySetResult();
                        throw;
                    }

                    return CommandResult.None;
                },
                cancellationToken
            );
        host.Start();
        child.Start();

        long id = 0;
        Task<JsonElement?> call = host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            framed =>
            {
                id = framed;
                return Serialize(new CommandInvokeRequest("wait", string.Empty, framed));
            },
            cancellationToken: CancellationToken.None
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await host.NotifyAsync(RemoteProtocol.CancelMethod, Cancel(id), Ct);

        JsonElement? reply = await call.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("cancelled", Disposition(reply!.Value));
        Assert.Equal(JsonValueKind.Null, reply.Value.GetProperty("output").ValueKind);
    }

    [Fact]
    public async Task A_cancel_for_a_finished_invoke_is_a_no_op()
    {
        (InMemoryLineChannel a, InMemoryLineChannel b) = InMemoryLineChannel.Pair();
        await using var host = new RemoteWire(a);
        await using var child = new RemoteWire(b);
        child.RequestReceived += (_, parameters, cancellationToken) =>
            RemoteCommandInvoke.DispatchAsync(
                Deserialize<CommandInvokeRequest>(parameters!.Value),
                (_, argument, _) => ValueTask.FromResult(CommandResult.Notice(argument)),
                cancellationToken
            );
        host.Start();
        child.Start();

        long finished = 0;
        JsonElement? first = await host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id =>
            {
                finished = id;
                return Serialize(new CommandInvokeRequest("ping", "one", id));
            },
            cancellationToken: Ct
        );
        Assert.Equal("completed", Disposition(first!.Value));

        // The invocation already answered: the cancel names an id the child no longer maps, and
        // the wire must contain it — no error frame, no torn connection.
        await host.NotifyAsync(RemoteProtocol.CancelMethod, Cancel(finished), Ct);

        JsonElement? second = await host.CallWithIdAsync(
            RemoteProtocol.CommandInvokeMethod,
            id => Serialize(new CommandInvokeRequest("ping", "two", id)),
            cancellationToken: Ct
        );
        Assert.Equal("completed", Disposition(second!.Value));
        Assert.Equal("two", second.Value.GetProperty("output").GetString());
        Assert.False(host.IsClosed);
    }

    internal static string Disposition(JsonElement reply) =>
        reply.GetProperty("disposition").GetString()!;

    internal static JsonElement Cancel(long id) =>
        JsonDocument
            .Parse(string.Create(CultureInfo.InvariantCulture, $$"""{"id":{{id}}}"""))
            .RootElement.Clone();

    internal static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, ScriptedChild.Options);

    internal static T Deserialize<T>(JsonElement element) =>
        JsonSerializer.Deserialize<T>(element, ScriptedChild.Options)
        ?? throw new InvalidOperationException($"{typeof(T).Name} deserialized to null");
}
