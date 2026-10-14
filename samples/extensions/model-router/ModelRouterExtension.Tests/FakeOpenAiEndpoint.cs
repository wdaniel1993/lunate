using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ModelRouterExtension.Tests;

/// <summary>A request the fake endpoint received.</summary>
internal sealed record RecordedRequest(string Authorization, string Model, bool OffersTools);

/// <summary>
/// A raw <see cref="TcpListener"/> OpenAI-compatible endpoint. HttpListener needs http.sys URL
/// ACLs on Windows, so this binds 127.0.0.1 on an ephemeral port (a recorded deviation from the
/// design's HttpListener wording) and serves one chat completion per connection, recording every
/// request's Authorization header, model and whether it offered tools.
/// </summary>
internal sealed class FakeOpenAiEndpoint : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public FakeOpenAiEndpoint()
    {
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        BaseUrl =
            $"http://127.0.0.1:{endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/v1";
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>The OpenAI-compatible base URL the declared provider endpoint uses.</summary>
    public string BaseUrl { get; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The accept loop ends by cancellation or listener shutdown; nothing to report.
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                (string headers, string body) = await ReadRequestAsync(stream, ct)
                    .ConfigureAwait(false);
                string authorization = Header(headers, "Authorization") ?? string.Empty;
                string model = ReadModel(body);
                bool offersTools = OffersTools(body);
                lock (_gate)
                {
                    _requests.Add(new RecordedRequest(authorization, model, offersTools));
                }

                byte[] payload = Encoding.UTF8.GetBytes(Completion(model));
                string responseHeaders = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"
                );
                await stream
                    .WriteAsync(Encoding.ASCII.GetBytes(responseHeaders), ct)
                    .ConfigureAwait(false);
                await stream.WriteAsync(payload, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A malformed or aborted request only matters through its absence in Requests.
            }
        }
    }

    private static async Task<(string Headers, string Body)> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken ct
    )
    {
        var head = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            head.WriteByte(one[0]);
            byte[] current = head.GetBuffer();
            int length = (int)head.Length;
            if (
                length >= 4
                && current[length - 4] == (byte)'\r'
                && current[length - 3] == (byte)'\n'
                && current[length - 2] == (byte)'\r'
                && current[length - 1] == (byte)'\n'
            )
            {
                break;
            }
        }

        string headers = Encoding.ASCII.GetString(head.ToArray());
        int contentLength = ReadContentLength(headers);
        var body = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            int read = await stream.ReadAsync(body.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return (headers, Encoding.UTF8.GetString(body, 0, offset));
    }

    private static int ReadContentLength(string headers)
    {
        string? value = Header(headers, "Content-Length");
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int length
        )
            ? length
            : 0;
    }

    private static string? Header(string headers, string name)
    {
        foreach (string line in headers.Split("\r\n"))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (
                colon > 0
                && string.Equals(line[..colon].Trim(), name, StringComparison.OrdinalIgnoreCase)
            )
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    private static string ReadModel(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out JsonElement model)
                && model.ValueKind == JsonValueKind.String
            )
            {
                return model.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall through to the default.
        }

        return "fast";
    }

    private static bool OffersTools(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("tools", out JsonElement tools)
                && tools.ValueKind == JsonValueKind.Array
                && tools.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Completion(string model) =>
        $$"""
            {
              "id": "chatcmpl-fake",
              "object": "chat.completion",
              "created": 0,
              "model": "{{model}}",
              "choices": [
                {
                  "index": 0,
                  "message": { "role": "assistant", "content": "reply from {{model}}" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 11, "completion_tokens": 4, "total_tokens": 15 }
            }
            """;
}
