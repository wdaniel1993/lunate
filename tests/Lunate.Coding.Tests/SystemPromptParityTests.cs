using Lunate.Agent;
using Lunate.Protocols.Acp;

namespace Lunate.Coding.Tests;

/// <summary>
/// The same launch directive must reach the model identically from every frontend: print, the
/// interactive session and ACP all compose through <see cref="SystemPrompt"/>.
/// </summary>
[Collection(SystemPromptDateCollection.Name)]
public sealed class SystemPromptParityTests
{
    [Fact]
    public async Task Print_interactive_and_acp_send_the_same_system_message()
    {
        using var temp = new TempDirectory();
        using var pin = new DatePin("2026-01-15");
        string root = new Workspace(temp.Root).WorktreeRoot;
        var launch = new LaunchOptions { AppendSystemPrompt = "shared appendix" };
        string expected = SystemPrompt.Compose(
            new Workspace(root),
            ["read", "write", "edit", "bash"],
            launch
        );

        string print = await PrintSystemMessageAsync(temp, root, launch);
        string interactive = await InteractiveSystemMessageAsync(root, launch);
        string acp = await AcpSystemMessageAsync(temp, root, launch);

        Assert.Equal(expected, print);
        Assert.Equal(expected, interactive);
        Assert.Equal(expected, acp);
    }

    private static async Task<string> PrintSystemMessageAsync(
        TempDirectory temp,
        string root,
        LaunchOptions launch
    )
    {
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = root,
            SessionDirectory = temp.File("print-sessions"),
            SessionsRoot = temp.File("print-sessions"),
            Launch = launch,
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        return client.Requests[0][0].Text;
    }

    private static async Task<string> InteractiveSystemMessageAsync(
        string root,
        LaunchOptions launch
    )
    {
        using var host = new InteractiveSessionHost(workingDirectory: root, launch: launch);
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("Hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        string message = host.Client.Requests[0][0].Text;
        host.Console.Complete();
        await run;
        return message;
    }

    private static async Task<string> AcpSystemMessageAsync(
        TempDirectory temp,
        string root,
        LaunchOptions launch
    )
    {
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        using var services = new WorkspaceServices(4);
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(root, null, null),
            null,
            [],
            [],
            services,
            launch,
            new FakeChatClientFactory(client),
            temp.File("acp-sessions")
        );

        await foreach (
            AgentEvent _ in harness.RunAsync("Hi", TestContext.Current.CancellationToken)
        ) { }

        return client.Requests[0][0].Text;
    }

    private sealed class DatePin : IDisposable
    {
        private readonly string? _previous;

        public DatePin(string value)
        {
            _previous = Environment.GetEnvironmentVariable("LUNATE_DATE");
            Environment.SetEnvironmentVariable("LUNATE_DATE", value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable("LUNATE_DATE", _previous);
    }
}
