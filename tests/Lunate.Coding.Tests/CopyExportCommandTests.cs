using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols;
using Lunate.Protocols.Acp;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>/copy</c> and <c>/export</c> through the real host: the transport the launch environment
/// picks, the notice each route earns, the default destination, the overwrite refusal, credential
/// redaction in both forms, the Ctrl+X catalog row, and that neither command exists outside the
/// interactive frontend.
/// </summary>
public sealed class CopyExportCommandTests
{
    private static readonly ClipboardPlatform Local = new(
        Remote: false,
        Tmux: false,
        ClipboardFamily.MacOs
    );

    [Fact]
    public async Task Copy_reports_the_tool_and_the_character_count()
    {
        var tool = new RecordingTool();
        var terminal = new List<string>();
        using var host = new InteractiveSessionHost(
            textClipboard: ClipboardFor(tool, Local, terminal.Add)
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");

        await SendLineAsync(host, "/copy");

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "copied 8 characters via pbcopy"
        );
        Assert.Equal("hi there", Assert.Single(tool.Texts));
        Assert.Empty(terminal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Copy_over_ssh_sends_the_sequence_and_starts_no_tool()
    {
        var tool = new RecordingTool();
        var terminal = new List<string>();
        using var host = new InteractiveSessionHost(
            textClipboard: ClipboardFor(
                tool,
                new ClipboardPlatform(Remote: true, Tmux: false, ClipboardFamily.MacOs),
                terminal.Add
            )
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");

        await SendLineAsync(host, "/copy");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "sent 8 characters to the terminal clipboard (OSC 52)"
        );
        Assert.Empty(tool.Texts);
        string sequence = Assert.Single(terminal);
        Assert.StartsWith("\u001b]52;c;", sequence, StringComparison.Ordinal);
        Assert.Equal(
            "hi there",
            System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(sequence["\u001b]52;c;".Length..^1])
            )
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ctrl_x_copies_the_last_answer()
    {
        var tool = new RecordingTool();
        using var host = new InteractiveSessionHost(
            textClipboard: ClipboardFor(tool, Local, _ => { })
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "answer two");

        host.Console.SendCtrl("x");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "copied 10 characters via pbcopy"
        );

        Assert.Equal("answer two", Assert.Single(tool.Texts));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Copy_with_no_completed_turn_reports_it_and_writes_nothing()
    {
        var tool = new RecordingTool();
        var terminal = new List<string>();
        using var host = new InteractiveSessionHost(
            textClipboard: ClipboardFor(tool, Local, terminal.Add)
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await SendLineAsync(host, "/copy");

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "nothing to copy: no completed answer yet"
        );
        Assert.Empty(tool.Texts);
        Assert.Empty(terminal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Copy_skips_a_cancelled_tail()
    {
        var tool = new RecordingTool();
        using var host = new InteractiveSessionHost(
            textClipboard: ClipboardFor(tool, Local, _ => { })
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "first", "first answer");

        host.Client.Gate(2);
        host.Console.SendText("second");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await host.WaitUntilAsync(() => host.Session.IsRunning);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        await SendLineAsync(host, "/copy");

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "copied 12 characters via pbcopy"
        );
        Assert.Equal("first answer", Assert.Single(tool.Texts));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Export_html_writes_the_default_path_and_names_it_with_the_review_notice()
    {
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        host.Scrollback.Profile.Width = 400;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");
        string sessionId = SessionId(host);

        await SendLineAsync(host, "/export html");

        string expected = Path.Combine(
            host.Temp.File("home"),
            ".lunate",
            "exports",
            "2026-10-24-" + sessionId + ".html"
        );
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "exported ");
        Assert.True(File.Exists(expected), expected);
        string html = File.ReadAllText(expected);
        Assert.Contains("hi there", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains($"exported {Entries(host):N0} entries to {expected}", host.ScrollbackText);
        Assert.Contains(
            "may contain file contents and command output; review before sharing",
            host.ScrollbackText
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Export_jsonl_with_a_path_override_writes_there()
    {
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");
        string destination = host.Temp.File("out.jsonl");

        await SendLineAsync(host, "/export jsonl " + destination);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "exported ");

        Assert.True(File.Exists(destination));
        Assert.Equal(File.ReadAllBytes(SessionFiles(host)[0]), File.ReadAllBytes(destination));
        Assert.Contains(
            $"exported {Entries(host):N0} entries to {Path.GetFullPath(destination)}",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Export_refuses_an_existing_destination_without_force()
    {
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi there");
        string destination = host.Temp.File("report.html");
        File.WriteAllText(destination, "keep me");

        await SendLineAsync(host, "/export html " + destination);

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "refusing to overwrite ");
        Assert.Contains("without --force", host.Frames, StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(destination));

        await SendLineAsync(host, "/export html " + destination + " --force");
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "exported ");

        Assert.Contains("<!DOCTYPE html>", File.ReadAllText(destination), StringComparison.Ordinal);
        Assert.Contains(
            $"exported {Entries(host):N0} entries to {Path.GetFullPath(destination)}",
            host.ScrollbackText
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Export_without_a_form_reports_the_usage()
    {
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "hi");

        await SendLineAsync(host, "/export");

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "usage: /export html|jsonl [path] [--force]"
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_known_provider_key_never_reaches_either_export()
    {
        const string Key = "sk-test-abcdef-012345";
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        File.WriteAllText(
            host.Temp.File("auth.json"),
            "{\"schemaVersion\":1,\"keys\":{\"openai\":\"" + Key + "\"}}"
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await TurnAsync(host, "hello", "the key is " + Key);
        string htmlDestination = host.Temp.File("report.html");
        string jsonlDestination = host.Temp.File("report.jsonl");

        await SendLineAsync(host, "/export html " + htmlDestination);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, htmlDestination);
        await SendLineAsync(host, "/export jsonl " + jsonlDestination);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, jsonlDestination);

        foreach (string path in new[] { htmlDestination, jsonlDestination })
        {
            string written = File.ReadAllText(path);
            Assert.DoesNotContain(Key, written, StringComparison.Ordinal);
            Assert.Contains("[redacted:openai]", written, StringComparison.Ordinal);
        }

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Help_and_the_key_overlay_name_both_routes()
    {
        using var host = Wide(new InteractiveSessionHost(clock: Clock));
        host.Scrollback.Profile.Width = 400;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await SendLineAsync(host, "/help");
        Assert.Contains("/copy", host.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("/export", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("?");
        await host.WaitForFrameAsync("Copy the last answer");

        host.Console.SendEscape();
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Print_mode_sends_the_commands_to_the_model_instead_of_resolving_them()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "/export html",
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "/export html",
            client
                .Requests[0]
                .Last(message => message.Role == Microsoft.Extensions.AI.ChatRole.User)
                .Text
        );
        Assert.False(Directory.Exists(Path.Combine(temp.File("home"), ".lunate", "exports")));
    }

    [Fact]
    public async Task Acp_sends_the_commands_to_the_model_instead_of_resolving_them()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        var workspace = new Workspace(temp.Root);
        using var services = new WorkspaceServices(4);
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(workspace.WorktreeRoot, null, null),
            null,
            [],
            [],
            services,
            new LaunchOptions { Model = PrintModeTestSupport.ModelId },
            new FakeChatClientFactory(client),
            temp.File("sessions")
        );

        await foreach (
            AgentEvent _ in harness.RunAsync("/copy", TestContext.Current.CancellationToken)
        ) { }

        Assert.Equal(
            "/copy",
            client
                .Requests[0]
                .Last(message => message.Role == Microsoft.Extensions.AI.ChatRole.User)
                .Text
        );
    }

    private static readonly Func<DateTimeOffset> Clock = () =>
        new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    private static ITextClipboard ClipboardFor(
        RecordingTool tool,
        ClipboardPlatform platform,
        Action<string> terminal
    ) => new ClipboardWriter(_ => null, tool, writeTerminal: terminal, platform: platform);

    /// <summary>The recording writer's terminal sink is the host's console; built per host below.</summary>
    private sealed class RecordingTool : IClipboardTool
    {
        public List<string> Texts { get; } = [];

        public Task<bool> WriteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string text,
            CancellationToken cancellationToken
        )
        {
            Texts.Add(text);
            return Task.FromResult(true);
        }
    }

    private static async Task TurnAsync(InteractiveSessionHost host, string input, string answer)
    {
        int before = host.Session.Transcript.Count;
        host.Client.Enqueue(Scripts.Text(answer), Scripts.Stop());
        host.Console.SendText(input);
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning && host.Session.Transcript.Count >= before + 2
        );
    }

    private static async Task SendLineAsync(InteractiveSessionHost host, string line)
    {
        await host.SendLineAsync(line);
        host.Advance(50);
    }

    private static InteractiveSessionHost Wide(InteractiveSessionHost host)
    {
        host.Console.Size = new ConsoleSize(200, 24);
        // The scrollback commits at the console width; a path must not wrap mid-line.
        host.Scrollback.Profile.Width = 400;
        return host;
    }

    private static string[] SessionFiles(InteractiveSessionHost host) =>
        Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl");

    private static string SessionId(InteractiveSessionHost host) =>
        Session.Load(Assert.Single(SessionFiles(host))).SessionId;

    private static int Entries(InteractiveSessionHost host) =>
        Session.Load(Assert.Single(SessionFiles(host))).Entries.Count;
}
