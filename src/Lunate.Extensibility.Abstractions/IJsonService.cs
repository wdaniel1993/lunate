using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A service callable over the JSON boundary: the remote extension host routes
/// <c>service/&lt;name&gt;/&lt;method&gt;</c> requests from a remote child, and host → child
/// <c>service-call</c> requests, to this contract. Services are async-only — there is no
/// synchronous invocation shape, so an in-process consumer can never hit a remote service on a
/// synchronous hot path. A method that is unknown, or arguments that do not fit it, become a
/// JSON-RPC error result, never a crash.
/// </summary>
public interface IJsonService
{
    /// <summary>
    /// Invokes one method by name with JSON arguments; the result is the method's JSON value
    /// (null allowed). Throwing maps to a JSON-RPC error result on the wire.
    /// </summary>
    ValueTask<JsonElement?> InvokeAsync(
        string method,
        JsonElement? arguments,
        CancellationToken cancellationToken
    );
}
