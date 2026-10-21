using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// Conditional registration of the optional search tools: off by default, additive, and never able
/// to shadow a core tool. The print-mode tests prove the resolved settings and launch list reach
/// the factory.
/// </summary>
public sealed class OptionalToolRegistrationTests
{
    [Fact]
    public void Nothing_is_registered_when_the_list_is_empty()
    {
        using var temp = new TempDirectory();

        ToolRegistry tools = HarnessFactory.CreateTools(new Workspace(temp.Root));

        Assert.Equal(["read", "write", "edit", "bash"], Names(tools));
    }

    [Theory]
    [InlineData("grep")]
    [InlineData("find")]
    [InlineData("ls")]
    public void Each_name_registers_exactly_its_tool(string name)
    {
        using var temp = new TempDirectory();

        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(temp.Root),
            extraTools: [name]
        );

        Assert.Equal(["read", "write", "edit", "bash", name], Names(tools));
        Assert.Equal(ToolRisk.ReadOnly, tools.Find(name)!.Risk);
    }

    [Fact]
    public void Several_names_register_in_the_given_order()
    {
        using var temp = new TempDirectory();

        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(temp.Root),
            extraTools: ["grep", "ls"]
        );

        Assert.Equal(["read", "write", "edit", "bash", "grep", "ls"], Names(tools));
        Assert.Null(tools.Find("find"));
    }

    [Fact]
    public void An_extra_name_cannot_shadow_a_core_tool()
    {
        using var temp = new TempDirectory();

        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(temp.Root),
            extraTools: ["bash", "grep"]
        );

        Assert.Single(tools.Tools, tool => tool.Name == "bash");
        Assert.Equal(ToolRisk.Execute, tools.Find("bash")!.Risk);
        Assert.NotNull(tools.Find("grep"));
    }

    [Fact]
    public void An_unknown_name_is_not_registered()
    {
        using var temp = new TempDirectory();

        ToolRegistry tools = HarnessFactory.CreateTools(
            new Workspace(temp.Root),
            extraTools: ["wat"]
        );

        Assert.Equal(["read", "write", "edit", "bash"], Names(tools));
    }

    [Fact]
    public void The_workspace_factory_offers_the_extras_to_child_roots()
    {
        using var temp = new TempDirectory();
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        Func<string, ToolRegistry> factory = HarnessFactory.CreateWorkspaceTools(
            services,
            [],
            extraTools: ["find"]
        );

        ToolRegistry tools = factory(temp.Root);

        Assert.NotNull(tools.Find("find"));
        Assert.NotNull(tools.Find("read"));
    }

    [Fact]
    public async Task Print_mode_exposes_a_tool_enabled_by_settings()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": { "extra": ["grep"] }
            }
            """
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        IReadOnlyList<string> names = Names(client.RequestOptions[0]);
        Assert.Contains("grep", names);
        Assert.Contains("read", names);
        Assert.DoesNotContain("find", names);
    }

    [Fact]
    public async Task The_launch_tools_override_the_environment_list()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            Environment = name =>
                name switch
                {
                    "LUNATE_MODEL" => PrintModeTestSupport.ModelId,
                    "LUNATE_EXTRA_TOOLS" => "grep",
                    _ => null,
                },
            Launch = new LaunchOptions { ExtraTools = ["ls"] },
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        IReadOnlyList<string> names = Names(client.RequestOptions[0]);
        Assert.Contains("ls", names);
        Assert.DoesNotContain("grep", names);
    }

    private static List<string> Names(ToolRegistry tools) =>
        [.. tools.Tools.Select(tool => tool.Name)];

    private static IReadOnlyList<string> Names(Microsoft.Extensions.AI.ChatOptions? options) =>
        [.. (options?.Tools ?? []).Select(tool => tool.Name ?? string.Empty)];
}
