using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class ToolOutputSpillReadTests
{
    [Fact]
    public async Task A_large_spill_can_be_read_to_completion_in_bounded_pages()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string full = string.Concat(Enumerable.Repeat("🙂", 50_000));
        Assert.Equal(50_000, ToolOutput.EstimateTokens(full));
        var client = new SpillPagingChatClient();
        var tools = new ToolRegistry();
        tools.Add(new LargeOutputTool(full));
        tools.Add(new ReadTool(new Workspace(workspace)));
        var harness = new AgentHarness(
            client,
            tools,
            new AgentHarnessOptions
            {
                Session = session,
                WorkingDirectory = workspace,
                SpillThresholdTokens = 2_000,
            }
        );

        await foreach (
            AgentEvent _ in harness.RunAsync(
                "read the large spill",
                TestContext.Current.CancellationToken
            )
        ) { }

        Assert.NotEmpty(client.ReadResults);
        Assert.True(client.ReadResults.Count > 1);
        Assert.All(
            client.ReadResults,
            page =>
            {
                Assert.DoesNotContain("full output at ", page, StringComparison.Ordinal);
                Assert.True(ToolOutput.EstimateTokens(page) <= 2_000);
            }
        );
        string joined = string.Concat(client.ReadResults.Select(SpillPagingChatClient.PageContent));
        Assert.Equal(Encoding.UTF8.GetBytes(full), Encoding.UTF8.GetBytes(joined));
    }

    [Fact]
    public async Task A_session_does_not_allow_a_forged_file_in_its_spill_directory()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string spillDirectory = ToolOutputSpill.DirectoryFor(session);
        Directory.CreateDirectory(spillDirectory);
        string forged = Path.Combine(spillDirectory, "forged-1.txt");
        File.WriteAllText(forged, "not a spill");
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, forged, workspace, session);

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spill_path_is_readable_in_full()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string full = new('x', 4_000);
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, "run", 1, full);
        MarkKnownSpill(session, spill.Path!);
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, spill.Path!, workspace, session);

        Assert.False(result.IsError);
        Assert.Equal(full, result.Output);
    }

    [Fact]
    public async Task A_resumed_session_can_read_its_spill_path()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string full = new('y', 4_000);
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, "run", 1, full);
        MarkKnownSpill(session, spill.Path!);
        Session resumed = Session.Load(session.Path);
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, spill.Path!, workspace, resumed);

        Assert.False(result.IsError);
        Assert.Equal(full, result.Output);
    }

    [Fact]
    public async Task A_session_does_not_allow_a_sibling_path_outside_tool_output()
    {
        using var temp = new TempDirectory();
        string workspace = CreateWorkspace(temp);
        Session session = CreateSession(temp, workspace);
        string sessionDirectory = Path.Combine(
            Path.GetDirectoryName(session.Path)!,
            session.SessionId
        );
        Directory.CreateDirectory(sessionDirectory);
        string sibling = Path.Combine(sessionDirectory, "sibling.txt");
        File.WriteAllText(sibling, "not a spill");
        var tool = new ReadTool(new Workspace(workspace));

        ToolResult result = await ReadAsync(tool, sibling, workspace, session);

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace", result.Output, StringComparison.Ordinal);
    }

    private static string CreateWorkspace(TempDirectory temp)
    {
        string workspace = temp.File("workspace");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static Session CreateSession(TempDirectory temp, string workspace) =>
        Session.Create(temp.File("sessions/session.jsonl"), workspace);

    private static void MarkKnownSpill(Session session, string path) =>
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("call_spill", $"full output at {path}]")]
            )
        );

    private static Task<ToolResult> ReadAsync(
        ReadTool tool,
        string path,
        string workingDirectory,
        Session? session
    )
    {
        using JsonDocument args = JsonDocument.Parse(JsonSerializer.Serialize(new { path }));
        return tool.ExecuteAsync(
            args.RootElement,
            new ToolContext(workingDirectory, new NullAgentEvents()) { Session = session },
            CancellationToken.None
        );
    }

    private sealed class LargeOutputTool(string output) : ITool
    {
        private static readonly JsonElement Schema = JsonDocument
            .Parse("{\"type\":\"object\"}")
            .RootElement.Clone();

        public string Name => "source";

        public string Description => "Returns a large test result.";

        public JsonElement ParametersSchema => Schema;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult(output, IsError: false));
    }

    private sealed class SpillPagingChatClient : IChatClient
    {
        private string? _spillPath;
        private int _callId = 1;

        public List<string> ReadResults { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            string? previousOutput = messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .LastOrDefault()
                ?.Result?.ToString();

            if (_spillPath is null && previousOutput is null)
            {
                yield return Scripts.Call("call_1", "source", Scripts.Args());
            }
            else
            {
                if (_spillPath is null)
                {
                    _spillPath = SpillPath(previousOutput!);
                }
                else if (previousOutput is not null)
                {
                    ReadResults.Add(previousOutput);
                }

                if (_spillPath is not null && previousOutput is not null && _callId > 1)
                {
                    if (
                        previousOutput.Contains("full output at ", StringComparison.Ordinal)
                        || NextOffset(previousOutput) is not { } nextOffset
                    )
                    {
                        yield return Scripts.Text("Done");
                        yield return Scripts.Stop();
                        yield break;
                    }

                    Dictionary<string, object?> arguments = Scripts.Args(("path", _spillPath));
                    arguments["offset"] = nextOffset;
                    yield return Scripts.Call($"call_{++_callId}", "read", arguments);
                }
                else
                {
                    yield return Scripts.Call(
                        $"call_{++_callId}",
                        "read",
                        Scripts.Args(("path", _spillPath))
                    );
                }
            }

            yield return Scripts.ToolCalls();
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private static string SpillPath(string output)
        {
            const string marker = "full output at ";
            int start = output.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException(
                    "The initial tool result had no spill pointer."
                );
            }

            start += marker.Length;
            int end = output.IndexOf(']', start);
            return end > start
                ? output[start..end]
                : throw new InvalidOperationException("The spill pointer had no closing bracket.");
        }

        private static int? NextOffset(string output)
        {
            const string marker = "[read continuation: offset ";
            int start = output.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            int end = output.IndexOf(']', start);
            return
                end > start
                && int.TryParse(
                    output.AsSpan(start, end - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int offset
                )
                ? offset
                : throw new InvalidOperationException("The read continuation offset was invalid.");
        }

        internal static string PageContent(string output)
        {
            const string marker = "\n[read continuation: offset ";
            int start = output.LastIndexOf(marker, StringComparison.Ordinal);
            return start < 0 ? output : output[..start];
        }
    }
}
