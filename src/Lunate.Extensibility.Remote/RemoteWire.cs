using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>Handles one incoming request; a thrown exception becomes an error response.</summary>
public delegate ValueTask<JsonElement?> RemoteRequestHandler(
    string method,
    JsonElement? parameters,
    CancellationToken cancellationToken
);

/// <summary>Handles one incoming notification.</summary>
public delegate ValueTask RemoteNotificationHandler(
    string method,
    JsonElement? parameters,
    CancellationToken cancellationToken
);

/// <summary>Configuration for <see cref="RemoteWire"/>.</summary>
public sealed record RemoteWireOptions
{
    /// <summary>The default per-call timeout; <see cref="Timeout.InfiniteTimeSpan"/> disables it.</summary>
    public TimeSpan DefaultCallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The greatest framed line accepted, in bytes; an oversized line is a protocol error.</summary>
    public int MaxLineBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Where wire diagnostics go (protocol errors, dropped responses).</summary>
    public IExtensionLog? Log { get; init; }
}

/// <summary>
/// One endpoint of the newline-delimited JSON-RPC 2.0 wire: compact UTF-8 frames, one object per
/// line, no batches. It correlates outgoing calls with separate id spaces per direction, allows
/// concurrent calls, maps <c>$/cancelRequest</c> to <see cref="CancellationToken"/> in both
/// directions, and turns handler exceptions into error responses. A non-JSON or oversized line,
/// or any malformed frame, is a protocol error: the connection ends and in-flight calls fail
/// immediately — never a best-effort reinterpretation.
/// </summary>
public sealed class RemoteWire : IAsyncDisposable
{
    private const int ErrorParse = -32700;
    private const int ErrorInvalidRequest = -32600;
    private const int ErrorMethodNotFound = -32601;
    private const int ErrorInternal = -32603;
    private const int ErrorCancelled = RemoteProtocol.CancelledErrorCode;

    private readonly IRemoteLineChannel _channel;
    private readonly RemoteWireOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonElement?>> _pending = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, CancellationTokenSource> _incoming = new(
        StringComparer.Ordinal
    );
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private long _nextId;
    private bool _disposed;

    public RemoteWire(IRemoteLineChannel channel, RemoteWireOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        _options = options ?? new RemoteWireOptions();
    }

    /// <summary>Incoming requests (any method except the reserved <c>$/cancelRequest</c>).</summary>
    public event RemoteRequestHandler? RequestReceived;

    /// <summary>Incoming notifications other than <c>$/cancelRequest</c>.</summary>
    public event RemoteNotificationHandler? NotificationReceived;

    /// <summary>Raised once when a protocol error ends the connection.</summary>
    public event Action<RemoteProtocolException>? ProtocolFailed;

    /// <summary>Completes when the connection ends (end of stream, protocol error or disposal).</summary>
    public Task Closed => _closed.Task;

    /// <summary>True once the connection ended.</summary>
    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed.Task.IsCompleted;
            }
        }
    }

    /// <summary>Starts the read loop; must be called exactly once before the first call.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = RunReadLoopAsync();
    }

    /// <summary>
    /// Sends one request and waits for its response. On cancellation or timeout the wire sends
    /// <c>$/cancelRequest</c> for the id and abandons the call; a late response is ignored.
    /// </summary>
    public Task<JsonElement?> CallAsync(
        string method,
        JsonElement? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) => CallCoreAsync(method, _ => parameters, timeout, cancellationToken);

    /// <summary>
    /// Sends one request whose payload must name the request's own id: the id is allocated first,
    /// <paramref name="parameters"/> builds the params from it, and the frame is written under it.
    /// A command invocation uses this so the id it carries is the id <c>$/cancelRequest</c> names.
    /// </summary>
    public Task<JsonElement?> CallWithIdAsync(
        string method,
        Func<long, JsonElement?> parameters,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) => CallCoreAsync(method, parameters, timeout, cancellationToken);

    private async Task<JsonElement?> CallCoreAsync(
        string method,
        Func<long, JsonElement?> parameters,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        long id = Interlocked.Increment(ref _nextId);
        string key = id.ToString(CultureInfo.InvariantCulture);
        var pending = new TaskCompletionSource<JsonElement?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lock (_gate)
        {
            if (_closed.Task.IsCompleted)
            {
                throw new RemoteChannelClosedException(ClosedReason());
            }

            _pending[key] = pending;
        }

        try
        {
            await WriteFrameAsync(Frame.Request(id, method, parameters(id)), cancellationToken)
                .ConfigureAwait(false);

            TimeSpan wait = timeout ?? _options.DefaultCallTimeout;
            Task<JsonElement?> response = pending.Task;
            if (wait == Timeout.InfiniteTimeSpan)
            {
                response = cancellationToken.CanBeCanceled
                    ? response.WaitAsync(cancellationToken)
                    : response;
            }
            else
            {
                response = response.WaitAsync(wait, cancellationToken);
            }

            return await response.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await AbandonAsync(key, id).ConfigureAwait(false);
            throw;
        }
        catch (TimeoutException)
        {
            await AbandonAsync(key, id).ConfigureAwait(false);
            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the extension did not answer '{method}' within {timeout ?? _options.DefaultCallTimeout}."
                )
            );
        }
        catch
        {
            lock (_gate)
            {
                _pending.Remove(key);
            }

            throw;
        }
    }

    /// <summary>Sends one notification (no response, no id).</summary>
    public async ValueTask NotifyAsync(
        string method,
        JsonElement? parameters = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await WriteFrameAsync(Frame.Notification(method, parameters), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        Close(new RemoteChannelClosedException("the wire was disposed."));
        try
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception) { }

        _lifetime.Dispose();
        _writeGate.Dispose();
    }

    private async Task RunReadLoopAsync()
    {
        RemoteProtocolException? protocolError = null;
        Exception? failure = null;
        try
        {
            while (true)
            {
                string? line = await _channel.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                HandleLine(line);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (RemoteProtocolException exception)
        {
            protocolError = exception;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (protocolError is { } protocol)
        {
            _options.Log?.Error($"the extension broke the wire protocol: {protocol.Message}");
            ProtocolFailed?.Invoke(protocol);
        }

        Close(
            protocolError is { } broken
                ? new RemoteChannelClosedException(broken.Message, broken)
                : new RemoteChannelClosedException(
                    failure is { } error
                        ? $"the extension's channel failed: {error.Message}"
                        : "the extension's output ended."
                )
        );
    }

    private void HandleLine(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) > _options.MaxLineBytes)
        {
            throw new RemoteProtocolException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"a line over {_options.MaxLineBytes} bytes arrived on the wire; the line was refused."
                )
            );
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            throw new RemoteProtocolException(
                $"a non-JSON line arrived on stdout: {Excerpt(line)}. Stdout is protocol-only."
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                throw new RemoteProtocolException(
                    "a JSON batch frame arrived; batches are not part of this wire."
                );
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new RemoteProtocolException("a frame that is not a JSON object arrived.");
            }

            if (
                !root.TryGetProperty("jsonrpc", out JsonElement version)
                || version.ValueKind != JsonValueKind.String
                || version.GetString() != "2.0"
            )
            {
                throw new RemoteProtocolException(
                    "a frame without \"jsonrpc\":\"2.0\" arrived; this wire is JSON-RPC 2.0."
                );
            }

            bool hasMethod =
                root.TryGetProperty("method", out JsonElement methodElement)
                && methodElement.ValueKind == JsonValueKind.String;
            bool hasId =
                root.TryGetProperty("id", out JsonElement idElement)
                && idElement.ValueKind is JsonValueKind.String or JsonValueKind.Number;
            bool hasResult = root.TryGetProperty("result", out JsonElement resultElement);
            bool hasError = root.TryGetProperty("error", out JsonElement errorElement);

            if (hasMethod)
            {
                string method = methodElement.GetString()!;
                JsonElement? parameters =
                    root.TryGetProperty("params", out JsonElement paramsElement)
                    && paramsElement.ValueKind != JsonValueKind.Null
                        ? paramsElement.Clone()
                        : null;
                if (method == "$/cancelRequest")
                {
                    // The target rides in params.id (the JSON-RPC cancelRequest convention; the
                    // frame is normally a notification, which carries no top-level id). A
                    // malformed cancel must never throw or tear the wire down.
                    JsonElement? target =
                        parameters is { ValueKind: JsonValueKind.Object } body
                        && body.TryGetProperty("id", out JsonElement targetElement)
                        && targetElement.ValueKind is JsonValueKind.String or JsonValueKind.Number
                            ? targetElement.Clone()
                            : null;
                    if (target is { } targetId)
                    {
                        HandleCancel(targetId);
                    }

                    if (hasId)
                    {
                        RespondWithResult(idElement.Clone(), null);
                    }

                    return;
                }

                if (hasId)
                {
                    DispatchRequest(idElement.Clone(), method, parameters);
                }
                else
                {
                    DispatchNotification(method, parameters);
                }

                return;
            }

            if (hasResult || hasError)
            {
                if (!hasId)
                {
                    throw new RemoteProtocolException("a response frame carries no id.");
                }

                if (hasResult && hasError)
                {
                    throw new RemoteProtocolException(
                        "a response frame carries both a result and an error."
                    );
                }

                HandleResponse(idElement, hasResult ? resultElement.Clone() : null, errorElement);
                return;
            }

            throw new RemoteProtocolException(
                "a frame carries neither a method nor a result/error."
            );
        }
    }

    private void HandleResponse(JsonElement id, JsonElement? result, JsonElement error)
    {
        string key = id.GetRawText();
        TaskCompletionSource<JsonElement?>? pending;
        lock (_gate)
        {
            _pending.Remove(key, out pending);
        }

        if (pending is null)
        {
            // A response to an abandoned (cancelled) or unknown request; JSON-RPC says ignore.
            return;
        }

        if (result is { } value)
        {
            pending.SetResult(value);
            return;
        }

        if (error.ValueKind != JsonValueKind.Object)
        {
            throw new RemoteProtocolException("an error response carries a non-object error.");
        }

        int code =
            error.TryGetProperty("code", out JsonElement codeElement)
            && codeElement.ValueKind == JsonValueKind.Number
            && codeElement.TryGetInt32(out int parsedCode)
                ? parsedCode
                : ErrorInternal;
        string message =
            error.TryGetProperty("message", out JsonElement messageElement)
            && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? "unknown error"
                : "unknown error";
        string? data =
            error.TryGetProperty("data", out JsonElement dataElement)
            && dataElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? dataElement.GetRawText()
                : null;
        pending.SetException(new RemoteCallException(code, message, data));
    }

    private void HandleCancel(JsonElement id)
    {
        CancellationTokenSource? incoming;
        lock (_gate)
        {
            _incoming.TryGetValue(id.GetRawText(), out incoming);
        }

        incoming?.Cancel();
    }

    private void DispatchRequest(JsonElement id, string method, JsonElement? parameters)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        lock (_gate)
        {
            _incoming[id.GetRawText()] = cts;
        }

        _ = Task.Run(async () =>
        {
            JsonObject response;
            try
            {
                RemoteRequestHandler? handler = RequestReceived;
                if (handler is null)
                {
                    throw new RemoteCallException(
                        ErrorMethodNotFound,
                        $"method '{method}' is not supported by this peer"
                    );
                }

                JsonElement? result = await handler(method, parameters, cts.Token)
                    .ConfigureAwait(false);
                response = Frame.Result(id, result);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                response = Frame.Error(id, ErrorCancelled, "the request was cancelled");
            }
            catch (RemoteCallException exception)
            {
                response = Frame.Error(
                    id,
                    exception.Code,
                    exception.PeerMessage,
                    exception.ErrorData
                );
            }
            catch (Exception exception)
            {
                response = Frame.Error(
                    id,
                    ErrorInternal,
                    $"{exception.GetType().FullName}: {exception.Message}"
                );
            }
            finally
            {
                lock (_gate)
                {
                    _incoming.Remove(id.GetRawText());
                }

                cts.Dispose();
            }

            try
            {
                await WriteFrameAsync(response, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The channel is gone; Close completes everything still in flight.
            }
        });
    }

    private void DispatchNotification(string method, JsonElement? parameters)
    {
        RemoteNotificationHandler? handler = NotificationReceived;
        if (handler is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await handler(method, parameters, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _options.Log?.Warn(
                    $"an incoming notification '{method}' failed: {exception.GetType().FullName}: {exception.Message}"
                );
            }
        });
    }

    private void RespondWithResult(JsonElement id, JsonElement? result)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteFrameAsync(Frame.Result(id, result), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception) { }
        });
    }

    private async Task WriteFrameAsync(JsonNode frame, CancellationToken cancellationToken)
    {
        string line = frame.ToJsonString();
        if (Encoding.UTF8.GetByteCount(line) > _options.MaxLineBytes)
        {
            throw new RemoteProtocolException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"an outgoing frame exceeds {_options.MaxLineBytes} bytes; it was not sent."
                )
            );
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _channel.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask AbandonAsync(string key, long id)
    {
        lock (_gate)
        {
            _pending.Remove(key);
        }

        try
        {
            await NotifyAsync("$/cancelRequest", Frame.CanceledId(id), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: the connection may already be gone.
        }
    }

    private string ClosedReason()
    {
        lock (_gate)
        {
            return _closedReason ?? "the extension's channel is closed.";
        }
    }

    private string? _closedReason;

    private void Close(RemoteChannelClosedException reason)
    {
        List<TaskCompletionSource<JsonElement?>> pending;
        List<CancellationTokenSource> incoming;
        lock (_gate)
        {
            if (_closed.Task.IsCompleted)
            {
                return;
            }

            _closedReason = reason.Message;
            pending = [.. _pending.Values];
            _pending.Clear();
            incoming = [.. _incoming.Values];
            _incoming.Clear();
        }

        foreach (TaskCompletionSource<JsonElement?> call in pending)
        {
            call.TrySetException(reason);
        }

        foreach (CancellationTokenSource cts in incoming)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        _closed.TrySetResult();
    }

    private static string Excerpt(string line) => line.Length <= 120 ? line : line[..120] + "…";

    private static class Frame
    {
        public static JsonObject Request(long id, string method, JsonElement? parameters)
        {
            var frame = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
            };
            if (parameters is { } value)
            {
                frame["params"] = JsonNode.Parse(value.GetRawText());
            }

            return frame;
        }

        public static JsonObject Notification(string method, JsonElement? parameters)
        {
            var frame = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
            if (parameters is { } value)
            {
                frame["params"] = JsonNode.Parse(value.GetRawText());
            }

            return frame;
        }

        public static JsonObject Result(JsonElement id, JsonElement? result) =>
            new()
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["result"] = result is { } value ? JsonNode.Parse(value.GetRawText()) : null,
            };

        public static JsonObject Error(
            JsonElement id,
            int code,
            string message,
            string? data = null
        )
        {
            var error = new JsonObject { ["code"] = code, ["message"] = message };
            if (data is not null)
            {
                error["data"] = data;
            }

            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["error"] = error,
            };
        }

        public static JsonElement CanceledId(long id)
        {
            using var document = JsonDocument.Parse(
                $$"""{"id":{{id.ToString(CultureInfo.InvariantCulture)}}}"""
            );
            return document.RootElement.Clone();
        }
    }
}
