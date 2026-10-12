using System.Globalization;
using System.Text.Json.Nodes;

namespace LspDiagnosticsExtension;

internal sealed partial class LspClient
{
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
        await _writeGate.WaitAsync(ct);
        try
        {
            await JsonRpcFraming.WriteMessageAsync(message, _output, ct);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
