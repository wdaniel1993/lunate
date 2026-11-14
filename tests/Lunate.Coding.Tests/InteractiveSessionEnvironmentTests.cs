using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The session-level visibility of a selected environment: the welcome header names it on its own
/// line from the first frame, and <c>/context</c> reports its readiness — <c>starting</c> before
/// the container exists, because the container is created by the first tool command.
/// </summary>
public sealed class InteractiveSessionEnvironmentTests
{
    [Fact]
    public async Task The_header_and_context_name_the_selected_environment()
    {
        using var host = new InteractiveSessionHost(environment: EnvironmentWithKey());
        host.Console.Size = new ConsoleSize(400, 24);
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":2,"environment":"podman:alpine","bashGuard":false}"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string header = await host.WaitForScrollbackAsync("environment podman:alpine");

        Assert.Contains("environment podman:alpine", header, StringComparison.Ordinal);

        host.Console.SendText("/context");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "environment: ");

        Assert.Contains(
            "environment: podman:alpine (starting)",
            host.LastFrame,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_local_default_adds_no_environment_line()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":2,"bashGuard":false}"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string header = await host.WaitForScrollbackAsync($"lunate  v{ProductVersion.Build}");

        Assert.DoesNotContain("environment ", header, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static Func<string, string?> EnvironmentWithKey()
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name =>
            name switch
            {
                "LUNATE_UNICODE" => "1",
                "OPENAI_API_KEY" => "test-key",
                _ => baseline(name),
            };
    }
}
