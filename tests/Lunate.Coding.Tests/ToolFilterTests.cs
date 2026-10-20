using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The per-run tool filter (design D1): exact model-facing names only; every other registry entry —
/// hidden tools and the inherited workspace list included — leaves the run.
/// </summary>
public sealed class ToolFilterTests
{
    [Fact]
    public void The_filter_keeps_only_the_requested_model_facing_tools()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("write", ToolExposure.Direct));

        Assert.True(
            HarnessFactory.TryFilterTools(
                tools,
                null,
                new LaunchOptions { Tools = ["read"] },
                out string? error
            ),
            error
        );

        Assert.Equal(["read"], tools.Tools.Select(tool => tool.Name));
        Assert.Equal(["read"], tools.Declarations.Select(declaration => declaration.Name));
    }

    [Fact]
    public void An_unknown_tool_name_is_an_error_listing_the_available_names()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("write", ToolExposure.Direct));

        Assert.False(
            HarnessFactory.TryFilterTools(
                tools,
                null,
                new LaunchOptions { Tools = ["nope"] },
                out string? error
            )
        );

        Assert.Equal("--tools names unknown tools: 'nope'; available: read, write", error);
        Assert.Equal(["read", "write"], tools.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void The_filter_matches_names_case_sensitively()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));

        Assert.False(
            HarnessFactory.TryFilterTools(
                tools,
                null,
                new LaunchOptions { Tools = ["Read"] },
                out string? error
            )
        );

        Assert.Equal("--tools names unknown tools: 'Read'; available: read", error);
    }

    [Fact]
    public void A_hidden_tool_is_not_available_and_cannot_be_requested()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("internal", ToolExposure.Hidden));

        Assert.False(
            HarnessFactory.TryFilterTools(
                tools,
                null,
                new LaunchOptions { Tools = ["internal"] },
                out string? error
            )
        );

        Assert.Equal("--tools names unknown tools: 'internal'; available: read", error);
    }

    [Fact]
    public void A_hidden_tool_leaves_the_registry_with_the_other_unrequested_tools()
    {
        var tools = new ToolRegistry();
        ITool read = new ProbeTool("read", ToolExposure.Direct);
        tools.Add(read);
        tools.Add(new ProbeTool("internal", ToolExposure.Hidden));

        Assert.True(
            HarnessFactory.TryFilterTools(
                tools,
                null,
                new LaunchOptions { Tools = ["read"] },
                out string? error
            ),
            error
        );

        Assert.Equal(["read"], tools.Tools.Select(tool => tool.Name));
        Assert.Null(tools.Find("internal"));
    }

    [Fact]
    public void No_tools_removes_every_registry_and_inherited_tool()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("internal", ToolExposure.Hidden));
        var inherited = new List<ITool> { new ProbeTool("read", ToolExposure.Direct) };

        Assert.True(
            HarnessFactory.TryFilterTools(
                tools,
                inherited,
                new LaunchOptions { NoTools = true },
                out string? error
            ),
            error
        );

        Assert.Empty(tools.Tools);
        Assert.Empty(tools.Declarations);
        Assert.Empty(inherited);
    }

    [Fact]
    public void The_filter_also_prunes_the_inherited_tool_list()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("tools__echo", ToolExposure.Direct));
        ITool extension = tools.Find("tools__echo")!;
        var inherited = new List<ITool> { extension };

        Assert.True(
            HarnessFactory.TryFilterTools(
                tools,
                inherited,
                new LaunchOptions { Tools = ["read"] },
                out string? error
            ),
            error
        );

        Assert.Empty(inherited);
    }

    [Fact]
    public void No_requested_list_keeps_the_composed_registry()
    {
        var tools = new ToolRegistry();
        tools.Add(new ProbeTool("read", ToolExposure.Direct));
        tools.Add(new ProbeTool("write", ToolExposure.Direct));
        ITool read = tools.Find("read")!;
        var inherited = new List<ITool> { read };

        Assert.True(HarnessFactory.TryFilterTools(tools, inherited, new LaunchOptions(), out _));

        Assert.Equal(["read", "write"], tools.Tools.Select(tool => tool.Name));
        Assert.Same(read, Assert.Single(inherited));
    }

    [Fact]
    public async Task Interactive_launch_tools_filter_the_request_declarations()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Tools = ["read"] });
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal(["read"], Names(host.Client.RequestOptions[0]));

        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Interactive_no_tools_exposes_no_declarations()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { NoTools = true });
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Empty(Names(host.Client.RequestOptions[0]));

        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Interactive_unknown_tool_ends_composition_with_the_usage_message()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Tools = ["nope"] });
        host.Console.Complete();

        await host.RunAsync();

        Assert.Contains(
            "--tools names unknown tools: 'nope'",
            host.Diagnostics,
            StringComparison.Ordinal
        );
        Assert.Contains("available: read, write", host.Diagnostics, StringComparison.Ordinal);
        Assert.False(host.Session.IsComposed);
    }

    private static IReadOnlyList<string> Names(ChatOptions? options) =>
        [.. (options?.Tools ?? []).Select(tool => tool.Name ?? string.Empty)];

    private sealed class ProbeTool(string name, ToolExposure exposure) : ITool
    {
        public string Name { get; } = name;

        public string Description => "A filter probe.";

        public JsonElement ParametersSchema { get; } =
            JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

        public ToolExposure Exposure { get; } = exposure;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("ok", false));
    }
}
