using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing.Tests;

/// <summary>
/// The kit composes a real extension's registered tools through the production loader: declared
/// with the prefixed name and authored schema, invoked with the real ToolContext (file-change
/// publish, nested ExecuteToolAsync ids, exposure gates, annotations, teaching errors) and
/// unloaded with ToolsChanged fired and in-flight calls cancelled (design D12).
/// </summary>
public sealed class ExtensionToolKitTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_registered_tool_is_declared_and_runs_with_the_real_context()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(
            host,
            """{"tools":[{"name":"echo","description":"Echoes.","schema":{"type":"object","properties":{"q":{"type":"string"}}}}]}"""
        );
        await host.StartAsync(Ct);

        ExtensionToolAdapter tool = Assert.Single(host.ComposeTools());

        Assert.Equal("tools__echo", tool.Name);
        Assert.Equal("Echoes.", tool.Description);
        Assert.Equal(
            """{"type":"object","properties":{"q":{"type":"string"}}}""",
            tool.ParametersSchema.GetRawText()
        );

        ExtensionRunResult result = await host.RunAsync(
            new ToolCallingChatClient(
                "tools__echo",
                new Dictionary<string, object?> { ["q"] = "hello" }
            ),
            "go",
            Ct
        );

        ToolCallResult call = Assert.Single(
            result.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is null
        );
        Assert.False(call.IsError);
        Assert.Contains("""args={"q":"hello"}""", call.Output, StringComparison.Ordinal);
        Assert.Contains($"cwd={host.WorkingDirectory}", call.Output, StringComparison.Ordinal);
        Assert.Contains("run=run_", call.Output, StringComparison.Ordinal);
        Assert.Contains("call=call-1", call.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_change_published_by_a_handler_lands_on_the_bus_with_the_full_payload()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(host, """{"tools":[{"name":"publish","behavior":"publish"}]}""");
        await host.StartAsync(Ct);
        host.ComposeTools();
        Assert.True(
            host.Runner.Services.TryGetCore<IFileChangeBus>(
                "core/file-bus",
                out IFileChangeBus? bus
            )
        );
        List<FileChangedPayload> changes = [];
        using IDisposable subscription = bus!.Subscribe(new CollectingFileChangedHandler(changes));

        await host.RunAsync(
            new ToolCallingChatClient("tools__publish", new Dictionary<string, object?>()),
            "go",
            Ct
        );

        FileChangedPayload change = Assert.Single(changes);
        Assert.Equal(Path.Combine(host.WorkingDirectory, "published.txt"), change.Path);
        Assert.Equal("published.txt", change.RelativePath);
        Assert.Equal(host.WorkingDirectory, change.WorkspaceId);
        Assert.Matches(@"run_[0-9a-f]{32}", change.RunId);
        Assert.Equal("modified", change.Kind);
    }

    [Fact]
    public async Task Two_runs_on_one_host_deliver_both_runs_file_changes()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(host, """{"tools":[{"name":"publish","behavior":"publish"}]}""");
        await host.StartAsync(Ct);
        host.ComposeTools();
        Assert.True(
            host.Runner.Services.TryGetCore<IFileChangeBus>(
                "core/file-bus",
                out IFileChangeBus? bus
            )
        );
        List<FileChangedPayload> changes = [];
        using IDisposable subscription = bus!.Subscribe(new CollectingFileChangedHandler(changes));
        string published = Path.Combine(host.WorkingDirectory, "published.txt");

        ExtensionRunResult first = await host.RunAsync(
            new ToolCallingChatClient("tools__publish", new Dictionary<string, object?>()),
            "go",
            Ct
        );
        ExtensionRunResult second = await host.RunAsync(
            new ToolCallingChatClient("tools__publish", new Dictionary<string, object?>()),
            "go",
            Ct
        );

        Assert.Equal([published, published], changes.Select(change => change.Path));
        Assert.Equal(
            [
                Assert.Single(first.Events.OfType<RunStarted>()).RunId,
                Assert.Single(second.Events.OfType<RunStarted>()).RunId,
            ],
            changes.Select(change => change.RunId)
        );
        Assert.DoesNotContain(
            host.Log.Entries,
            entry => entry.Message.Contains("a drain has begun", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_nested_execute_tool_call_uses_slash_ids_and_the_real_pipeline()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(host, """{"tools":[{"name":"nest","behavior":"nested"}]}""");
        host.Tools.Add(new TestTool("read", "contents"));
        await host.StartAsync(Ct);
        host.ComposeTools();

        ExtensionRunResult result = await host.RunAsync(
            new ToolCallingChatClient("tools__nest", new Dictionary<string, object?>()),
            "go",
            Ct
        );

        ToolCallStart nested = Assert.Single(
            result.Events.OfType<ToolCallStart>(),
            start => start.ParentToolCallId is not null
        );
        Assert.Equal("call-1/1", nested.CallId);
        Assert.Equal("call-1", nested.ParentToolCallId);
        Assert.Equal("read", nested.ToolName);
        ToolCallResult nestedResult = Assert.Single(
            result.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is not null
        );
        Assert.Equal("call-1/1", nestedResult.CallId);
        ToolCallResult call = Assert.Single(
            result.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is null
        );
        Assert.Equal("nested read: contents", call.Output);
    }

    [Fact]
    public async Task Exposure_gates_the_declarations_and_nested_visibility()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(
            host,
            """
            {"tools":[
              {"name":"visible"},
              {"name":"programmatic","exposure":"programmatic"},
              {"name":"hidden","exposure":"hidden"},
              {"name":"nestP","exposure":"programmatic","behavior":"nested","nestedTarget":"tools__programmatic"},
              {"name":"nestH","exposure":"programmatic","behavior":"nested","nestedTarget":"tools__hidden"}
            ]}
            """
        );
        await host.StartAsync(Ct);
        host.ComposeTools();

        Assert.Equal(
            ["tools__visible"],
            host.Tools.Declarations.Select(declaration => declaration.Name)
        );

        ExtensionRunResult programmatic = await host.RunAsync(
            new ToolCallingChatClient("tools__nestP", new Dictionary<string, object?>()),
            "go",
            Ct
        );
        Assert.Contains(
            "nested tools__programmatic: echo:",
            Assert
                .Single(
                    programmatic.Events.OfType<ToolCallResult>(),
                    candidate => candidate.ParentToolCallId is null
                )
                .Output,
            StringComparison.Ordinal
        );

        ExtensionRunResult hidden = await host.RunAsync(
            new ToolCallingChatClient("tools__nestH", new Dictionary<string, object?>()),
            "go",
            Ct
        );
        ToolCallResult hiddenResult = Assert.Single(
            hidden.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is null
        );
        Assert.True(hiddenResult.IsError);
        Assert.Contains(
            "not callable programmatically",
            hiddenResult.Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Annotations_reach_the_tool_calling_hook()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(host, """{"tools":[{"name":"careful","annotations":{"destructive":true}}]}""");
        List<IReadOnlyList<string>> observed = [];
        host.Runner.Hooks.Register(
            "observe",
            new LocalToolCallingHandler(
                (payload, _) =>
                {
                    observed.Add(payload.Annotations);
                    return ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Proceed(null)
                    );
                }
            )
        );
        await host.StartAsync(Ct);
        host.ComposeTools();

        await host.RunAsync(
            new ToolCallingChatClient("tools__careful", new Dictionary<string, object?>()),
            "go",
            Ct
        );

        Assert.Equal(["destructive"], Assert.Single(observed));
    }

    [Fact]
    public async Task A_throwing_handler_becomes_the_teaching_error_and_the_run_survives()
    {
        using var temp = new TempDirectory();
        await using var host = Host(temp);
        Settings(host, """{"tools":[{"name":"explode","behavior":"throw"}]}""");
        await host.StartAsync(Ct);
        host.ComposeTools();

        ExtensionRunResult result = await host.RunAsync(
            new ToolCallingChatClient("tools__explode", new Dictionary<string, object?>()),
            "go",
            Ct
        );

        ToolCallResult call = Assert.Single(
            result.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is null
        );
        Assert.True(call.IsError);
        Assert.Contains(
            "Tool 'tools__explode' failed: the scripted tool exploded. Fix the call or try a different approach.",
            call.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(
            StopReasons.Stop,
            Assert.Single(result.Events.OfType<RunFinished>()).StopReason
        );
    }

    [Fact]
    public async Task Unload_removes_the_tools_fires_tools_changed_and_cancels_an_in_flight_call()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("started.marker");
        await using var host = Host(temp);
        Settings(
            host,
            $$"""{"tools":[{"name":"block","behavior":"block","marker":{{System.Text.Json.JsonSerializer.Serialize(marker)}}},{"name":"echo"}]}"""
        );
        List<ToolsChangedPayload> changes = [];
        host.Runner.Hooks.Register(
            "observe",
            new LocalToolsChangedHandler(payload =>
            {
                lock (changes)
                {
                    changes.Add(payload);
                }
            })
        );
        await host.StartAsync(Ct);
        host.ComposeTools();

        Task<ExtensionRunResult> run = host.RunAsync(
            new ToolCallingChatClient("tools__block", new Dictionary<string, object?>()),
            "go",
            Ct
        );
        await WaitForFileAsync(marker);
        await host.UnloadAsync(Ct);
        ExtensionRunResult result = await run;

        ToolCallResult call = Assert.Single(
            result.Events.OfType<ToolCallResult>(),
            candidate => candidate.ParentToolCallId is null
        );
        Assert.True(call.IsError);
        Assert.Contains("unloaded", call.Output, StringComparison.Ordinal);
        Assert.Empty(host.Tools.Tools);
        Assert.Equal(2, changes.Count);
        Assert.Equal(["tools__block", "tools__echo"], changes[0].Tools);
        Assert.Empty(changes[1].Tools);
    }

    private const string DeclaredTools =
        "\"echo\",\"publish\",\"nest\",\"nestP\",\"nestH\",\"visible\",\"programmatic\",\"hidden\",\"careful\",\"explode\",\"block\"";

    private static ExtensionTestHost Host(TempDirectory temp) =>
        new(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "tools",
                ExtensionDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "TestExtensions",
                    "ToolExtension"
                ),
                SourceFiles = new Dictionary<string, string>
                {
                    ["extension.json"] =
                        $$"""{"id":"tools","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"ToolExtension.dll","tools":[{{DeclaredTools}}]}""",
                },
            }
        );

    private static void Settings(ExtensionTestHost host, string settings)
    {
        string directory = Path.Combine(host.StorePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "tools.json"), settings);
    }

    private static async Task WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 500 && !File.Exists(path); attempt++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(File.Exists(path), $"the handler did not start; expected marker '{path}'.");
    }

    private sealed class CollectingFileChangedHandler(List<FileChangedPayload> changes)
        : IFileChangedHandler
    {
        public int Priority => 0;

        public ValueTask OnFileChangedAsync(
            FileChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            changes.Add(payload);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LocalToolCallingHandler(
        Func<ToolCallingPayload, CancellationToken, ValueTask<ToolCallingResult>> handle
    ) : IToolCallingHandler
    {
        public int Priority => 0;

        public ValueTask<ToolCallingResult> HandleAsync(
            ToolCallingPayload payload,
            CancellationToken cancellationToken
        ) => handle(payload, cancellationToken);
    }

    private sealed class LocalToolsChangedHandler(Action<ToolsChangedPayload> handle)
        : IToolsChangedHandler
    {
        public int Priority => 0;

        public ValueTask ObserveAsync(
            ToolsChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            handle(payload);
            return ValueTask.CompletedTask;
        }
    }
}
