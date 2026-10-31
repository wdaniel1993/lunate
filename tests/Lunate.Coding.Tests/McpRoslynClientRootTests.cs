using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace Lunate.Coding.Tests;

public sealed class McpRoslynClientRootTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activation_tracks_client_roots_until_a_supported_change()
    {
        using var temp = new TempDirectory();
        string nonmatchingRoot = Path.Combine(temp.Root, "client-root-without-project");
        Directory.CreateDirectory(nonmatchingRoot);
        File.WriteAllText(temp.File("Launch.slnx"), "<Solution />");
        string clientRoot = Path.Combine(temp.Root, "client-root");
        Directory.CreateDirectory(clientRoot);
        File.WriteAllText(Path.Combine(clientRoot, "App.csproj"), "<Project />");
        File.WriteAllText(
            Path.Combine(clientRoot, "Program.cs"),
            "public class ClientRootExample { }"
        );
        var roots = new List<string>
        {
            new Uri(nonmatchingRoot + Path.DirectorySeparatorChar).AbsoluteUri,
        };
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var errors = new StringWriter();
        Task<int> run = Task.Run(() =>
            McpRoslynCommand.RunAsync(
                temp.Root,
                toServer.Reader.AsStream(),
                toClient.Writer.AsStream(),
                errors,
                CancellationToken.None
            )
        );
        using var reader = new StreamReader(toClient.Reader.AsStream(), Encoding.UTF8);
        using var writer = new StreamWriter(toServer.Writer.AsStream(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

        await RequestAsync(
            reader,
            writer,
            1,
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                clientInfo = new { name = "test-client", version = "0.0.1" },
                capabilities = new { roots = new { listChanged = true } },
            },
            roots
        );
        JsonElement inactive = await RequestAsync(reader, writer, 2, "tools/list", new { }, roots);
        Assert.Empty(inactive.GetProperty("result").GetProperty("tools").EnumerateArray());

        roots.Clear();
        roots.Add(new Uri(clientRoot + Path.DirectorySeparatorChar).AbsoluteUri);
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(
                new { jsonrpc = "2.0", method = "notifications/roots/list_changed" }
            )
        );
        JsonElement changed = await WaitForNotificationAsync(
            reader,
            writer,
            roots,
            "notifications/tools/list_changed"
        );
        Assert.Equal("notifications/tools/list_changed", changed.GetProperty("method").GetString());

        JsonElement active = await RequestAsync(reader, writer, 3, "tools/list", new { }, roots);
        Assert.Contains(
            "cs_outline",
            active
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()),
            StringComparer.Ordinal
        );
        JsonElement call = await RequestAsync(
            reader,
            writer,
            4,
            "tools/call",
            new { name = "cs_outline", arguments = new { file = "Program.cs" } },
            roots
        );
        Assert.Contains(
            "ClientRootExample",
            call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal
        );

        roots.Clear();
        JsonElement stale = await RequestAsync(
            reader,
            writer,
            5,
            "tools/call",
            new { name = "cs_outline", arguments = new { file = "Program.cs" } },
            roots
        );
        Assert.False(stale.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains(
            "ClientRootExample",
            stale.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal
        );

        await writer.DisposeAsync();
        await toServer.Writer.CompleteAsync();
        Assert.Equal(0, await run.WaitAsync(WaitBudgets.InProcess, Ct));
    }

    private static async Task<JsonElement> RequestAsync(
        StreamReader reader,
        StreamWriter writer,
        int id,
        string method,
        object parameters,
        IReadOnlyList<string> roots
    )
    {
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params = parameters,
                }
            )
        );
        while (true)
        {
            JsonElement frame = await ReadFrameAsync(reader);
            if (await RespondToRootsListAsync(writer, frame, roots))
            {
                continue;
            }

            if (frame.TryGetProperty("id", out JsonElement frameId) && frameId.GetInt32() == id)
            {
                return frame;
            }
        }
    }

    private static async Task<JsonElement> WaitForNotificationAsync(
        StreamReader reader,
        StreamWriter writer,
        IReadOnlyList<string> roots,
        string method
    )
    {
        while (true)
        {
            JsonElement frame = await ReadFrameAsync(reader);
            if (await RespondToRootsListAsync(writer, frame, roots))
            {
                continue;
            }

            if (
                frame.TryGetProperty("method", out JsonElement serverMethod)
                && serverMethod.GetString() == method
            )
            {
                return frame;
            }
        }
    }

    private static async Task<bool> RespondToRootsListAsync(
        StreamWriter writer,
        JsonElement frame,
        IReadOnlyList<string> roots
    )
    {
        if (
            !frame.TryGetProperty("method", out JsonElement method)
            || method.GetString() != "roots/list"
        )
        {
            return false;
        }

        await writer.WriteLineAsync(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = frame.GetProperty("id"),
                    result = new
                    {
                        roots = roots
                            .Select(uri => new Dictionary<string, string> { ["uri"] = uri })
                            .ToArray(),
                    },
                }
            )
        );
        return true;
    }

    private static async Task<JsonElement> ReadFrameAsync(StreamReader reader)
    {
        string? line = await reader.ReadLineAsync(Ct).AsTask().WaitAsync(WaitBudgets.InProcess, Ct);
        Assert.NotNull(line);
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }
}
