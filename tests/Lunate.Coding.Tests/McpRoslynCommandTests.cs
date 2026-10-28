using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>lunate mcp roslyn</c>'s composition: the five <c>cs_*</c> tools are listed even where no
/// solution exists, and a call reaches the tool's own no-solution answer instead of failing the
/// server. The backend is a seam here so the test does not depend on MSBuild loading.
/// </summary>
public sealed class McpRoslynCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Serves_the_five_roslyn_tools_and_maps_a_call_to_the_backend()
    {
        using var temp = new TempDirectory();
        var backend = new NoSolutionBackend();
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var errors = new StringWriter();
        Task<int> run = Task.Run(() =>
            McpRoslynCommand.RunAsync(
                temp.Root,
                toServer.Reader.AsStream(),
                toClient.Writer.AsStream(),
                errors,
                CancellationToken.None,
                () => backend
            )
        );

        using var reader = new StreamReader(toClient.Reader.AsStream(), Encoding.UTF8);
        using var writer = new StreamWriter(toServer.Writer.AsStream(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

        JsonElement initialize = await RequestAsync(
            reader,
            writer,
            1,
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                clientInfo = new { name = "test-client", version = "0.0.1" },
                capabilities = new { },
            }
        );
        Assert.Equal(
            McpRoslynCommand.ServerName,
            initialize
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("name")
                .GetString()
        );

        JsonElement list = await RequestAsync(reader, writer, 2, "tools/list", new { });
        string[] names =
        [
            .. list.GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            ["cs_diagnostics", "cs_find_references", "cs_find_symbol", "cs_outline", "cs_rename"],
            names
        );

        JsonElement call = await RequestAsync(
            reader,
            writer,
            3,
            "tools/call",
            new { name = "cs_find_symbol", arguments = new { name = "Calculator" } }
        );
        JsonElement result = call.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "No C# solution is available",
            result.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal
        );
        Assert.Equal(1, backend.Loads);

        await writer.DisposeAsync();
        await toServer.Writer.CompleteAsync();
        Assert.Equal(0, await run.WaitAsync(WaitBudgets.InProcess, Ct));
        Assert.Contains("lunate mcp roslyn", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_names_roslyn_and_rejects_an_unknown_server()
    {
        using var errors = new StringWriter();

        int exitCode = McpCommand.Run(["other"], errors, Ct);

        Assert.Equal(2, exitCode);
        Assert.Contains("unknown MCP server 'other'", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("Usage: lunate mcp roslyn", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_lists_the_mcp_command()
    {
        using var output = new StringWriter();

        int exitCode = Cli.Run(["--help"], output, TextWriter.Null, Ct);

        Assert.Equal(0, exitCode);
        Assert.Contains("lunate mcp roslyn", output.ToString(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> RequestAsync(
        StreamReader reader,
        StreamWriter writer,
        int id,
        string method,
        object parameters
    )
    {
        await writer
            .WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id,
                        method,
                        @params = parameters,
                    }
                )
            )
            .WaitAsync(WaitBudgets.InProcess, Ct);

        while (true)
        {
            string? line = await reader
                .ReadLineAsync(Ct)
                .AsTask()
                .WaitAsync(WaitBudgets.InProcess, Ct);
            Assert.NotNull(line);
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement frame = document.RootElement.Clone();
            Assert.Equal("2.0", frame.GetProperty("jsonrpc").GetString());
            if (frame.TryGetProperty("id", out JsonElement frameId) && frameId.GetInt32() == id)
            {
                return frame;
            }
        }
    }

    private sealed class NoSolutionBackend : ICSharpBackend
    {
        public int Loads { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
        {
            Loads++;
            return ValueTask.FromResult(
                new WorkspaceLoadResult(
                    WorkspaceStatus.NoSolution,
                    $"no solution found: no .sln or .slnx under '{Path.GetTempPath()}' or its immediate subdirectories",
                    null,
                    0,
                    0,
                    []
                )
            );
        }

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<RenamePlanResult> PlanRenameAsync(
            string name,
            string newName,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public void NotifyFileChanged(string absolutePath) { }

        public void NotifyProjectChanged() { }
    }
}
