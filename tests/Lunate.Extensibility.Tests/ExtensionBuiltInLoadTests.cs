using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The built-in front door (spec: Built-in extension provenance): the factory is in hand — the
/// host's own assemblies, so no collectible load context exists — no trust or install-record step
/// runs, the manifest cross-checks refuse like a discovered extension's, a failing built-in does
/// not take the loader down, and a built-in cannot be unloaded.
/// </summary>
public sealed class ExtensionBuiltInLoadTests
{
    [Fact]
    public async Task A_builtin_loads_through_its_factory_and_never_creates_a_load_context()
    {
        using var temp = new TempDirectory();
        var loader = Loader(temp, BuiltInOne("echo", tools: ["ping"]));

        loader.Discover(temp.Root, "repo");
        LoadedExtension loaded = await loader.Load(
            "echo",
            temp.Root,
            "repo",
            NoTrustPrompt(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("echo", loaded.Id);
        ExtensionToolAdapter tool = Assert.Single(loader.Tools.List());
        Assert.Equal("echo", tool.ExtensionId);
        Assert.Equal("echo__ping", tool.Name);
        Assert.Empty(loader.LoadContexts);
    }

    [Fact]
    public async Task A_builtin_load_never_prompts_for_trust_and_writes_no_record()
    {
        using var temp = new TempDirectory();
        var loader = Loader(temp, BuiltInOne("echo", tools: ["ping"]));
        var prompt = NoTrustPrompt();

        loader.Discover(temp.Root, "repo");
        await loader.Load("echo", temp.Root, "repo", prompt, TestContext.Current.CancellationToken);

        Assert.Equal(0, prompt.Calls);
        Assert.False(File.Exists(Path.Combine(temp.Subdirectory("store"), "trust.json")));
    }

    [Fact]
    public async Task A_registered_tool_the_builtin_manifest_does_not_declare_refuses_the_load()
    {
        using var temp = new TempDirectory();
        // The manifest declares nothing; the factory registers "ping" anyway.
        var loader = Loader(temp, BuiltInOne("echo", tools: [], registers: ["ping"]));

        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(
            async () =>
                await loader.Load(
                    "echo",
                    temp.Root,
                    "repo",
                    NoTrustPrompt(),
                    TestContext.Current.CancellationToken
                )
        );
        Assert.Contains(
            "not declared in the manifest",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(loader.IsLoaded("echo"));
        Assert.Empty(loader.Tools.List());
    }

    [Fact]
    public async Task A_failing_builtin_does_not_stop_a_later_builtin_from_loading()
    {
        using var temp = new TempDirectory();
        var bomb = new BuiltInExtension(
            "host/bomb/extension.json",
            Manifest("bomb", tools: []),
            () => throw new InvalidOperationException("boom")
        );
        var loader = Loader(temp, bomb, BuiltInOne("echo", tools: ["ping"]));

        loader.Discover(temp.Root, "repo");

        ExtensionLoadException failed = await Assert.ThrowsAsync<ExtensionLoadException>(async () =>
            await loader.Load(
                "bomb",
                temp.Root,
                "repo",
                NoTrustPrompt(),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("the built-in factory", failed.Message, StringComparison.Ordinal);
        Assert.Contains("boom", failed.Message, StringComparison.Ordinal);
        await loader.Load(
            "echo",
            temp.Root,
            "repo",
            NoTrustPrompt(),
            TestContext.Current.CancellationToken
        );
        Assert.False(loader.IsLoaded("bomb"));
        Assert.True(loader.IsLoaded("echo"));
        Assert.Equal("echo__ping", Assert.Single(loader.Tools.List()).Name);
    }

    [Fact]
    public async Task Unloading_a_builtin_is_refused_with_the_teaching_message()
    {
        using var temp = new TempDirectory();
        var loader = Loader(temp, BuiltInOne("echo", tools: ["ping"]));
        loader.Discover(temp.Root, "repo");
        await loader.Load(
            "echo",
            temp.Root,
            "repo",
            NoTrustPrompt(),
            TestContext.Current.CancellationToken
        );

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(
            async () =>
                await loader.Unload("echo")
        );

        Assert.Equal(
            "extension 'echo' is a built-in; built-ins load with the host and cannot be reloaded or unloaded.",
            exception.Message
        );
        Assert.True(loader.IsLoaded("echo"));
        Assert.Equal("echo__ping", Assert.Single(loader.Tools.List()).Name);
    }

    private static ExtensionLoader Loader(TempDirectory temp, params BuiltInExtension[] builtIns)
    {
        var list = new List<BuiltInExtension>();
        list.AddRange(builtIns);
        return new ExtensionLoader(
            new ExtensionHostOptions { StorePath = temp.Subdirectory("store"), BuiltIns = list }
        );
    }

    /// <summary>A built-in whose factory registers the tools it is told to — the manifest declares
    /// <paramref name="tools"/>, the factory registers <paramref name="registers"/> instead, so a
    /// test can make the cross-check fail.</summary>
    internal static BuiltInExtension BuiltInOne(
        string id,
        string[] tools,
        string[]? registers = null
    ) =>
        new(
            $"host/{id}/extension.json",
            Manifest(id, tools),
            () => new RegisteringFactory(registers ?? tools)
        );

    internal static ExtensionManifest Manifest(
        string id,
        string[] tools,
        string apiVersion = "^1.0.0"
    )
    {
        string declared = JsonSerializer.Serialize(tools.Length == 0 ? [] : tools);
        return ExtensionManifest.Parse(
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"{{apiVersion}}","builtin":true,"tools":{{declared}}}""",
            $"host/{id}/extension.json"
        );
    }

    private static FakeExtensionTrustPrompt NoTrustPrompt()
    {
        // Zero scripted approvals and record-keeping: the queue throws on any call, so a test that
        // passes while this prompt sits at zero calls proves the built-in door never asks.
        return new FakeExtensionTrustPrompt(approve: false);
    }
}

/// <summary>An extension whose factory only registers the tools it is given.</summary>
internal sealed class RegisteringFactory : IExtensionFactory
{
    private readonly string[] _tools;

    public RegisteringFactory(string[] tools) => _tools = tools;

    public IExtension Create(IExtensionContext context)
    {
        foreach (string tool in _tools)
        {
            context.RegisterTool(
                new ExtensionToolRegistration
                {
                    Name = tool,
                    Description = "a built-in test tool",
                    ParametersSchema = JsonSerializer.SerializeToElement(
                        new { type = "object", properties = Array.Empty<object>() }
                    ),
                    Handler = new StaticHandler($"{tool} ran"),
                }
            );
        }

        return new EmptyExtension();
    }
}

internal sealed class EmptyExtension : IExtension { }

internal sealed class StaticHandler(string answer) : IExtensionToolHandler
{
    public Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    ) => Task.FromResult(new ToolResult(answer, IsError: false));
}
