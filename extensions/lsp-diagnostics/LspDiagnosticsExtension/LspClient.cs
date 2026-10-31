using System.Globalization;
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
internal sealed partial class LspClient : IAsyncDisposable
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
    /// The LSP <c>languageId</c> is derived from the document's extension, so one server serves
    /// both the verifier and the tool-result hook.
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
                        ["languageId"] = LanguageId(absolutePath),
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

    /// <summary>LSP language identifiers for the extensions the supported servers accept.</summary>
    private static string LanguageId(string absolutePath) =>
        Path.GetExtension(absolutePath) switch
        {
            ".ts" => "typescript",
            ".tsx" => "typescriptreact",
            ".cs" => "csharp",
            _ => "plaintext",
        };

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

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                JsonNode? message = await JsonRpcFraming.ReadMessageAsync(_input, ct);
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
}
