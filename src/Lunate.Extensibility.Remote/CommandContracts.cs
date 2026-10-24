using System.Text.Json;
using System.Text.Json.Serialization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>
/// How a command invocation ended: <c>completed</c> (the handler answered), <c>cancelled</c> (the
/// cancellation reached the handler's token) or <c>failed</c> (the handler threw, or the child
/// could not run it). The names are the wire vocabulary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommandInvokeDisposition>))]
public enum CommandInvokeDisposition
{
    [JsonStringEnumMemberName("completed")]
    Completed,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>
/// One host → child command invocation: the bare command name (no <c>ext:</c> prefix, no leading
/// slash), the argument remainder and the request id the frame was sent under — the id
/// <c>$/cancelRequest</c> names to cancel it. A wire DTO of this package, never the public
/// <see cref="CommandInvocation"/>: the abstract surface can grow without moving the wire.
/// </summary>
public sealed record CommandInvokeRequest(string Name, string Argument, long RequestId);

/// <summary>
/// The child's answer: exactly the output text, the optional failure message and the disposition
/// of the in-process result — nothing else crosses back. A field the abstract result grows must
/// arrive here and in the conformance run, never silently on the wire.
/// </summary>
public sealed record CommandInvokeResult(
    string? Output,
    string? Error,
    CommandInvokeDisposition Disposition
);

/// <summary>One invoked command: the bare name, the argument remainder and the invocation's token.</summary>
public delegate ValueTask<CommandResult> RemoteCommandHandler(
    string name,
    string argument,
    CancellationToken cancellationToken
);

/// <summary>
/// The child-side entry for <c>command/invoke</c>: it runs the registered handler under the
/// request's token (the one <c>$/cancelRequest</c> reaches through the wire's incoming-request
/// map, which service calls use too), answers a cancellation as <c>cancelled</c> rather than
/// <c>failed</c>, and turns a throwing handler into a <c>failed</c> reply carrying its message.
/// </summary>
public static class RemoteCommandInvoke
{
    /// <summary>The failure message used when a handler fails without saying why.</summary>
    public const string FailureWithoutMessage = "the command failed without a message";

    /// <summary>The reply to a cancelled invocation: no output, no error, <c>cancelled</c>.</summary>
    public static CommandInvokeResult Cancelled { get; } =
        new(null, null, CommandInvokeDisposition.Cancelled);

    /// <summary>The wire reply for one in-process result, field by field.</summary>
    public static CommandInvokeResult Reply(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsError
            ? new(
                null,
                string.IsNullOrWhiteSpace(result.Message) ? FailureWithoutMessage : result.Message,
                CommandInvokeDisposition.Failed
            )
            : new(result.Message, null, CommandInvokeDisposition.Completed);
    }

    /// <summary>Serialises one reply as the frame's result.</summary>
    public static JsonElement Encode(CommandInvokeResult reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return JsonSerializer.SerializeToElement(reply, WireJson.Options);
    }

    /// <summary>The reply frame for an invocation the child does not know how to serve.</summary>
    public static JsonElement Failed(string message) =>
        Encode(
            new(
                null,
                string.IsNullOrWhiteSpace(message) ? FailureWithoutMessage : message,
                CommandInvokeDisposition.Failed
            )
        );

    /// <summary>
    /// Runs one invoked command and returns the reply frame — typed nullable so it can answer a
    /// <see cref="RemoteWire"/> request handler directly. The cancellation token is the one the
    /// wire cancelled for this request id; a handler that observes it answers <c>cancelled</c>.
    /// </summary>
    public static async ValueTask<JsonElement?> DispatchAsync(
        CommandInvokeRequest request,
        RemoteCommandHandler handler,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        CommandInvokeResult reply;
        try
        {
            reply = Reply(
                await handler(request.Name, request.Argument, cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reply = Cancelled;
        }
        catch (Exception exception)
        {
            reply = new(
                null,
                string.IsNullOrWhiteSpace(exception.Message)
                    ? FailureWithoutMessage
                    : exception.Message,
                CommandInvokeDisposition.Failed
            );
        }

        return Encode(reply);
    }
}
