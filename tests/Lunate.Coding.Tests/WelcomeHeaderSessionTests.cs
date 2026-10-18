using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The welcome header at the session seam (design D1/D4/D5): one print on the first frame before
/// composition output, the single counts update line, the first-run form, the setting and the
/// environment override, <c>/new</c> and the machine modes that never construct it.
/// </summary>
public sealed class WelcomeHeaderSessionTests
{
    private const string Mark = "\u03f9";
    private const string Hint = "/help · /model · /skills · /extensions · Esc cancels · /quit";

    [Fact]
    public async Task The_header_prints_once_on_the_first_frame()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        Assert.Equal(1, Occurrences(scrollback, Mark));
        Assert.Contains("lunate  v0.1.0", scrollback, StringComparison.Ordinal);
        Assert.Contains("gpt-4o-mini · 128,000 ctx", scrollback, StringComparison.Ordinal);
        // The header's lines are finished text; the test console wraps one longer than its own
        // width, so the workspace path is asserted on the unwrapped text.
        Assert.Contains(
            Workspace.Canonicalize(host.Temp.Root),
            scrollback.Replace("\n", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal
        );
        Assert.Contains(Hint, scrollback, StringComparison.Ordinal);
        // The header is finished scrollback, never part of the live frame (no splash, no redraw).
        Assert.DoesNotContain(Mark, host.Frames, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_csharp_workspace_is_named_in_the_header()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        File.WriteAllText(host.Temp.File("sample.csproj"), "<Project />");
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.Contains("C# workspace · Roslyn", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Composition_fills_the_counts_with_one_update_line()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        Assert.Contains("extensions … · MCP … · skills …", scrollback, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(scrollback, "extensions 0 · MCP 0 · skills 0"));
        Assert.Equal(1, Occurrences(scrollback, Mark));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_narrow_terminal_gets_the_single_line_form()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        host.Console.Size = new ConsoleSize(40, 24);
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        Assert.Contains("lunate v0.1.0 — /help for commands", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain(Mark, scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("extensions …", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_ascii_terminal_gets_the_single_line_form_in_ascii_punctuation()
    {
        using var host = new InteractiveSessionHost(
            environment: Environment(("LUNATE_ASCII", "1"), ("OPENAI_API_KEY", "test-key"))
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        Assert.Contains("lunate v0.1.0 - /help for commands", scrollback, StringComparison.Ordinal);
        Assert.True(
            scrollback.All(character => character <= 0x7F),
            "the ASCII form must be printable on a legacy console: " + scrollback
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_counts_fill_in_from_the_composition()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        WriteUserSkill(host.Temp.File("home"), "greek");
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.Contains(
            "extensions 0 · MCP 0 · skills 1",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_first_run_names_one_fix_and_no_counts_or_hint()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        Assert.Contains(
            "add the 'openai' key to ~/.lunate/auth.json",
            scrollback,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("extensions", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("Esc cancels", scrollback, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(scrollback, Mark));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_first_run_with_an_empty_key_variable_names_the_variable()
    {
        using var host = new InteractiveSessionHost(
            environment: Environment(("OPENAI_API_KEY", " "))
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.Contains(
            "set OPENAI_API_KEY to a valid key",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_startup_header_setting_suppresses_the_header()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{ "schemaVersion": 1, "startupHeader": false }"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        Assert.DoesNotContain(Mark, host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_startup_header_environment_variable_suppresses_the_header()
    {
        using var host = new InteractiveSessionHost(
            environment: Environment(("LUNATE_STARTUP_HEADER", "0"))
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        Assert.DoesNotContain(Mark, host.ScrollbackText, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task New_shows_the_header_again()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        Assert.Equal(2, Occurrences(host.ScrollbackText, Mark));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Print_mode_never_renders_the_header()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(Mark, output + errors, StringComparison.Ordinal);
        Assert.DoesNotContain("Esc cancels", output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_mode_never_renders_the_header()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Json = true,
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(Mark, output + errors, StringComparison.Ordinal);
        Assert.DoesNotContain("Esc cancels", output + errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Acp_mode_never_renders_the_header()
    {
        using var temp = new TempDirectory();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();
        Write(
            input,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientInfo":{"name":"test","version":"0.0.1"},"clientCapabilities":{}}}"""
        );
        Write(
            input,
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "session/new",
                    @params = new { cwd = temp.Root, mcpServers = Array.Empty<string>() },
                }
            )
        );
        input.Position = 0;

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            TestExtensionSeams.Empty(temp),
            mcpConfigPath: temp.File("global-mcp.json"),
            mcpAgentsConfigPath: temp.File("agents-mcp.json")
        );

        Assert.Equal(0, exitCode);
        string stdout = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("\"jsonrpc\"", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Mark, stdout + errors.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Esc cancels", stdout + errors.ToString(), StringComparison.Ordinal);
    }

    private static void Write(Stream stream, string line) =>
        stream.Write(System.Text.Encoding.UTF8.GetBytes(line + "\n"));

    private static int Occurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>A user-scope skill: discovered without a trust question.</summary>
    private static void WriteUserSkill(string home, string name)
    {
        string directory = Path.Combine(home, ".agents", "skills", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: Does a thing\n---\n\nBody of the skill.\n"
        );
    }

    private static Func<string, string?> KeyedEnvironment() =>
        Environment(("LUNATE_UNICODE", "1"), ("OPENAI_API_KEY", "test-key"));

    private static Func<string, string?> Environment(params (string Name, string Value)[] overrides)
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name =>
        {
            foreach ((string overridden, string value) in overrides)
            {
                if (string.Equals(name, overridden, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return baseline(name);
        };
    }
}
