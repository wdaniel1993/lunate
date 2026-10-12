using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace FakeLspServer;

/// <summary>Content-Length framed JSON-RPC messages over a byte stream, as LSP defines them.</summary>
internal static class JsonRpc
{
    public static async Task<JsonNode?> ReadAsync(Stream input, CancellationToken ct)
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

    public static async Task WriteAsync(Stream output, JsonNode message, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {payload.Length}\r\n\r\n")
        );
        await output.WriteAsync(header, ct);
        await output.WriteAsync(payload, ct);
        await output.FlushAsync(ct);
    }

    private static async Task<int> ReadByteAsync(Stream input, CancellationToken ct)
    {
        byte[] buffer = new byte[1];
        int read = await input.ReadAsync(buffer, ct);
        return read == 0 ? -1 : buffer[0];
    }
}
