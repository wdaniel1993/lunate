using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The loader stamps every registered tool's activation rule from the manifest (tasks 5.2): the
/// explicit <c>toolActivation</c> key wins, then the <c>*</c> blanket key, and no key at all means
/// no rule — the adapter's null the composition reads as always active.
/// </summary>
public sealed class ExtensionToolActivationStampingTests
{
    [Fact]
    public async Task A_declared_rule_is_stamped_on_its_tool_and_others_get_none()
    {
        using var temp = new TempDirectory();
        var loader = Loader(
            temp,
            Manifest(
                "echo",
                ["ping", "pong"],
                """{"ping":{"when":"workspace-root-has-file","patterns":["*.sln"]}}"""
            )
        );

        loader.Discover(temp.Root, "repo");
        await loader.Load("echo", temp.Root, "repo", Prompt(), Ct);

        ExtensionToolAdapter ping = Tool(loader, "echo__ping");
        ExtensionToolAdapter pong = Tool(loader, "echo__pong");
        Assert.Equal("workspace-root-has-file", ping.ActivationRule!.When);
        Assert.Equal(["*.sln"], ping.ActivationRule.Patterns);
        Assert.Null(pong.ActivationRule);
    }

    [Fact]
    public async Task The_star_key_stamps_a_blanket_rule()
    {
        using var temp = new TempDirectory();
        var loader = Loader(
            temp,
            Manifest("echo", ["ping", "pong"], """{"*":{"when":"always"}}""")
        );

        loader.Discover(temp.Root, "repo");
        await loader.Load("echo", temp.Root, "repo", Prompt(), Ct);

        Assert.All(
            Tools(loader, "echo"),
            tool => Assert.Equal("always", tool.ActivationRule!.When)
        );
    }

    [Fact]
    public async Task An_explicit_key_overrides_the_star_key()
    {
        using var temp = new TempDirectory();
        var loader = Loader(
            temp,
            Manifest(
                "echo",
                ["ping", "pong"],
                """{"*":{"when":"always"},"ping":{"when":"workspace-is-git-repository"}}"""
            )
        );

        loader.Discover(temp.Root, "repo");
        await loader.Load("echo", temp.Root, "repo", Prompt(), Ct);

        Assert.Equal(
            "workspace-is-git-repository",
            Tool(loader, "echo__ping").ActivationRule!.When
        );
        Assert.Equal("always", Tool(loader, "echo__pong").ActivationRule!.When);
    }

    [Fact]
    public async Task A_manifest_without_activation_leaves_the_rule_null()
    {
        using var temp = new TempDirectory();
        var loader = Loader(temp, ExtensionBuiltInLoadTests.Manifest("echo", ["ping"]));

        loader.Discover(temp.Root, "repo");
        await loader.Load("echo", temp.Root, "repo", Prompt(), Ct);

        ExtensionToolAdapter tool = Assert.Single(Tools(loader, "echo"));
        Assert.Null(tool.ActivationRule);
    }

    private static ExtensionToolAdapter Tool(ExtensionLoader loader, string name) =>
        Tools(loader, "echo")
            .Single(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    private static IReadOnlyList<ExtensionToolAdapter> Tools(ExtensionLoader loader, string id) =>
        [
            .. loader
                .Tools.List()
                .Where(tool => string.Equals(tool.ExtensionId, id, StringComparison.Ordinal)),
        ];

    private static ExtensionManifest Manifest(string id, string[] tools, string activation)
    {
        string declared = JsonSerializer.Serialize(tools.Length == 0 ? [] : tools);
        return ExtensionManifest.Parse(
            $$"""
            {"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","builtin":true,"tools":{{declared}},"toolActivation":{{activation}}}
            """,
            $"host/{id}/extension.json"
        );
    }

    private static ExtensionLoader Loader(TempDirectory temp, ExtensionManifest manifest) =>
        new(
            new ExtensionHostOptions
            {
                StorePath = temp.Subdirectory("store"),
                BuiltIns =
                [
                    new BuiltInExtension(
                        $"host/{manifest.Id}/extension.json",
                        manifest,
                        () => new RegisteringFactory([.. manifest.Tools])
                    ),
                ],
            }
        );

    private static FakeExtensionTrustPrompt Prompt() => new(approve: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
