using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace LspDiagnosticsExtension;

/// <summary>
/// A minimal LSP client over one server's stdio streams: Content-Length framed JSON-RPC, one
/// request in flight, per-document versions (<c>didOpen</c> v1, <c>didChange</c> vN with the full
/// text) and a bounded wait for the <c>publishDiagnostics</c> notification carrying the version
/// that was sent; stale versions are ignored and the wait continues. The owning
/// <see cref="LspServerService"/> bounds round trips and restarts dead processes.
/// </summary>
internal sealed class LspClient : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);
    private readonly Channel<Publish> _published = Channel.CreateUnbounded<Publish>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
    );
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _readLoop;
    private int _nextId;
    private Exception? _readError;
    private bool _disposed;

    public LspClient(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Starts the read loop; call once after constructing the process streams.</summary>
    public void Start() => _readLoop = Task.Run(() => ReadLoopAsync(_lifetime.Token));

    public async Task InitializeAsync(string rootPath, CancellationToken ct)
    {
        var parameters = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = FileUri.FromPath(rootPath),
            ["capabilities"] = new JsonObject(),
        };
        await SendRequestAsync("initialize", parameters, ct);
        await SendNotificationAsync("initialized", new JsonObject(), ct);
    }

    /// <summary>
    /// Opens or updates the document and waits for diagnostics carrying the version just sent.
    /// </summary>
    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string absolutePath,
        string text,
        CancellationToken ct
    )
    {
        string uri = FileUri.FromPath(absolutePath);
        int version = _versions.TryGetValue(uri, out int previous) ? previous + 1 : 1;
        _versions[uri] = version;

        if (version == 1)
        {
            await SendNotificationAsync(
                "textDocument/didOpen",
                new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = uri,
                        ["languageId"] = "csharp",
                        ["version"] = version,
                        ["text"] = text,
                    },
                },
                ct
            );
        }
        else
        {
            await SendNotificationAsync(
                "textDocument/didChange",
                new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
                    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
                },
                ct
            );
        }

        return await WaitForDiagnosticsAsync(uri, version, ct);
    }

    /// <summary>Best-effort orderly shutdown: a shutdown request bounded by the caller, then exit.</summary>
    public async Task ShutdownAsync(CancellationToken ct)
    {
        try
        {
            await SendRequestAsync("shutdown", new JsonObject(), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }

        try
        {
            await SendNotificationAsync("exit", null, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        if (_readLoop is { } loop)
        {
            try
            {
                await loop;
            }
            catch (Exception) { }
        }

        _lifetime.Dispose();
        _writeGate.Dispose();
    }

    private async Task<IReadOnlyList<Diagnostic>> WaitForDiagnosticsAsync(
        string uri,
        int version,
        CancellationToken ct
    )
    {
        while (true)
        {
            Publish published;
            try
            {
                published = await _published.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException) when (_readError is { } error)
            {
                throw new IOException(
                    $"the language server exited before publishing diagnostics: {error.Message}",
                    error
                );
            }
            catch (ChannelClosedException)
            {
                throw new IOException("the language server exited before publishing diagnostics.");
            }

            if (
                string.Equals(published.Uri, uri, StringComparison.Ordinal)
                && published.Version == version
            )
            {
                return published.Diagnostics;
            }
        }
    }

    private async Task<JsonNode?> SendRequestAsync(
        string method,
        JsonNode parameters,
        CancellationToken ct
    )
    {
        ThrowIfBroken();
        int id = Interlocked.Increment(ref _nextId);
        string key = id.ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonNode?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lock (_gate)
        {
            _pending[key] = completion;
        }

        using (ct.Register(() => completion.TrySetCanceled(ct)))
        {
            try
            {
                await WriteAsync(
                    new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id,
                        ["method"] = method,
                        ["params"] = parameters,
                    },
                    ct
                );
                return await completion.Task;
            }
            finally
            {
                lock (_gate)
                {
                    _pending.Remove(key);
                }
            }
        }
    }

    private Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken ct) =>
        WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters,
            },
            ct
        );

    private async Task WriteAsync(JsonNode message, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {payload.Length}\r\n\r\n")
        );
        await _writeGate.WaitAsync(ct);
        try
        {
            await _output.WriteAsync(header, ct);
            await _output.WriteAsync(payload, ct);
            await _output.FlushAsync(ct);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                JsonNode? message = await ReadMessageAsync(_input, ct);
                if (message is null)
                {
                    _readError = new IOException("the language server closed its output.");
                    return;
                }

                Dispatch(message);
            }

            _readError = new IOException("the client was disposed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _readError = new IOException("the client was disposed.");
        }
        catch (Exception exception)
        {
            _readError = exception;
        }
        finally
        {
            _published.Writer.TryComplete();
            FailPending();
        }
    }

    private void Dispatch(JsonNode message)
    {
        if (message["method"] is null && message["id"] is { } id && Key(id) is { } key)
        {
            TaskCompletionSource<JsonNode?>? completion;
            lock (_gate)
            {
                _pending.Remove(key, out completion);
            }

            if (completion is null)
            {
                return;
            }

            if (message["error"] is { } error)
            {
                completion.TrySetException(
                    new InvalidOperationException(
                        $"the language server returned an error: {error.ToJsonString()}"
                    )
                );
            }
            else
            {
                completion.TrySetResult(message["result"]?.DeepClone());
            }

            return;
        }

        if (message["method"]?.GetValue<string>() != "textDocument/publishDiagnostics")
        {
            return;
        }

        JsonNode? parameters = message["params"];
        string uri = parameters?["uri"]?.GetValue<string>() ?? string.Empty;
        int? version = parameters?["version"] is { } versionNode
            ? versionNode.GetValue<int>()
            : null;
        _published.Writer.TryWrite(new Publish(uri, version, ParseDiagnostics(parameters)));
    }

    private static List<Diagnostic> ParseDiagnostics(JsonNode? parameters)
    {
        var diagnostics = new List<Diagnostic>();
        if (parameters?["diagnostics"] is not JsonArray array)
        {
            return diagnostics;
        }

        foreach (JsonNode? node in array)
        {
            if (node is null)
            {
                continue;
            }

            int severity = node["severity"]?.GetValue<int>() ?? 0;
            int line = node["range"]?["start"]?["line"]?.GetValue<int>() ?? 0;
            int character = node["range"]?["start"]?["character"]?.GetValue<int>() ?? 0;
            string code = node["code"]?.ToString() ?? string.Empty;
            string message = node["message"]?.GetValue<string>() ?? string.Empty;
            diagnostics.Add(new Diagnostic(severity, line, character, code, message));
        }

        return diagnostics;
    }

    private void FailPending()
    {
        Exception failure = _readError ?? new IOException("the language server exited.");
        List<TaskCompletionSource<JsonNode?>> pending;
        lock (_gate)
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (TaskCompletionSource<JsonNode?> completion in pending)
        {
            completion.TrySetException(failure);
        }
    }

    private void ThrowIfBroken()
    {
        if (_readError is { } error)
        {
            throw new IOException($"the language server is not usable: {error.Message}", error);
        }
    }

    private static string? Key(JsonNode id) =>
        id is JsonValue value && value.TryGetValue(out long number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : id.GetValue<string>();

    private static async Task<JsonNode?> ReadMessageAsync(Stream input, CancellationToken ct)
    {
        int? length = null;
        var header = new StringBuilder();
        while (true)
        {
            int value = await ReadByteAsync(input, ct);
            if (value < 0)
            {
                return null;
            }

            if (value == '\n')
            {
                string line = header.ToString().TrimEnd('\r');
                header.Clear();
                if (line.Length == 0)
                {
                    break;
                }

                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    length = int.Parse(
                        line["Content-Length:".Length..].Trim(),
                        CultureInfo.InvariantCulture
                    );
                }

                continue;
            }

            header.Append((char)value);
        }

        if (length is not { } size || size < 0)
        {
            throw new InvalidDataException("the message has no Content-Length header");
        }

        byte[] payload = new byte[size];
        await input.ReadExactlyAsync(payload, ct);
        return JsonNode.Parse(payload);
    }

    private static async Task<int> ReadByteAsync(Stream input, CancellationToken ct)
    {
        byte[] buffer = new byte[1];
        int read = await input.ReadAsync(buffer, ct);
        return read == 0 ? -1 : buffer[0];
    }

    private sealed record Publish(string Uri, int? Version, IReadOnlyList<Diagnostic> Diagnostics);
}
