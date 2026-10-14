using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The registry-to-run wiring of Task 4 of sample-model-router: a composed session resolves
/// extension-registered models, and the interactive frontend shows the routing line only for
/// extension/virtual session models.
/// </summary>
public sealed class InteractiveSessionModelRoutingTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_composed_session_resolves_an_extension_registered_provider()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string store = temp.File("store");
        ModelFixture.Install(store);

        await using var host = await ExtensionSession.StartAsync(
            new Extensibility.Abstractions.WorkspaceInfo(worktree, null, null),
            new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            Ct
        );

        ChatResponse response = await host
            .ModelRegistry.Resolve("fixture-model")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], null, Ct);

        Assert.Equal("tagged:fixture reply", response.Text);
        Assert.NotNull(host.ModelRegistry.Resolve("ext/model-fixture/auto"));
    }

    [Fact]
    public async Task A_virtual_session_model_shows_the_last_route_in_the_footer_and_picker()
    {
        using var host = new InteractiveSessionHost(environment: _ => null);
        ModelFixture.Install(host.Temp.File("extensions-store"));
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":1,"model":"ext/model-fixture/auto"}"""
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "auto → fixture-model");

        host.Console.SendText("/model");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Select model — current auto → fixture-model"
        );

        host.Console.SendEscape();
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_misconfigured_extension_model_fails_with_one_clear_message()
    {
        using var host = new InteractiveSessionHost(environment: _ => null);
        ModelFixture.Install(host.Temp.File("extensions-store"));
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":1,"model":"ext/model-fixture/broken"}"""
        );

        // Composition surfaces the failure as one clear diagnostic line and the session ends
        // (a session cannot run without a model) — never a raw exception mid-composition.
        await host.RunAsync();

        Assert.Contains("ext/model-fixture/broken", host.Diagnostics, StringComparison.Ordinal);
        Assert.Contains("did not resolve", host.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_catalog_session_model_keeps_its_plain_footer_and_picker_title()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "gpt-4o-mini");
        Assert.DoesNotContain("→", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/model");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        Assert.DoesNotContain("current", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendEscape();
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
