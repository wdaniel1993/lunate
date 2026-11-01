using System.Text.Json;
using System.Text.RegularExpressions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The welcome header at the session seam (design D1/D4/D5): one print on the first frame before
/// composition output, the single counts update line, the first-run form (no model configured, no
/// key, an unresolved variable), the setting and the environment override, <c>/new</c> and the
/// machine modes that never construct it.
/// </summary>
public sealed class WelcomeHeaderSessionTests
{
    private const string Mark = "\u03f9";
    private const string Hint =
        "/help · /model · /skills · /extensions · ctrl+o transcript · Esc cancels · /quit";

    [Fact]
    public async Task The_header_prints_once_on_the_first_frame()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The header is the marker: waiting for its first line proves the whole print committed.
        string scrollback = await host.WaitForScrollbackAsync($"lunate  v{ProductVersion.Build}");
        Assert.Equal(1, Occurrences(scrollback, Mark));
        Assert.Contains($"lunate  v{ProductVersion.Build}", scrollback, StringComparison.Ordinal);
        Assert.Contains("gpt-4o-mini · 128,000 ctx", scrollback, StringComparison.Ordinal);
        // The header prints the workspace in the product's display form: a path under the user's
        // home is `~`-shortened (on Windows CI the test temp directory lives under the home). The
        // header's lines are finished text; the test console wraps one longer than its own width,
        // so the workspace path is asserted on the unwrapped text.
        Assert.Contains(
            DisplayPath.For(Workspace.Canonicalize(host.Temp.Root)),
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
    public async Task A_disabled_bash_guard_is_named_in_the_welcome_header()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion":2,"bashGuard":false}"""
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);
        string scrollback = await host.WaitForScrollbackAsync(
            text => text.Contains("bash guard disabled", StringComparison.Ordinal),
            "the disabled bash-guard header status"
        );

        Assert.Contains("bash guard disabled", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_loading_line_continues_under_the_header()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        WriteSkill(worktree, "greek");
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            workingDirectory: worktree
        );
        Task run = host.RunAsync();

        // The project-skill trust question holds composition open, so the live frame below the
        // header is still the loading line rather than the footer.
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );
        // The trust question painted after the header committed, so the mark must already be there.
        await host.WaitForScrollbackAsync(Mark);
        Assert.Contains("loading project…", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("n");
        await host.WaitUntilAsync(() => host.Session.IsComposed);

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

        await host.WaitForScrollbackAsync("C# workspace · Roslyn");

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

        // The counts line is the last row the composition writes: its snapshot is the frame the
        // placeholder and mark counts below are about.
        string scrollback = await host.WaitForScrollbackAsync("extensions 0 · MCP 0 · skills 0");
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

        // The header is one line at 40 columns, but the scrollback records the terminal's own wrap:
        // a version string long enough to cross that width arrives split between two words, so the
        // phrase is matched across the wrap instead of as one contiguous substring.
        string header =
            $"lunate v{ProductVersion.Build} · bash guard none · probe not run — /help for commands";
        string scrollback = await host.WaitForScrollbackAsync(
            text => ShowsPhrase(text, header),
            $"the header \"{header}\" (a wrap between words is fine)"
        );
        Assert.True(ShowsPhrase(scrollback, header), "the header must be present: " + scrollback);
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

        string header =
            $"lunate v{ProductVersion.Build} - bash guard none - probe not run - /help for commands";
        string scrollback = await host.WaitForScrollbackAsync(
            text => ShowsPhrase(text, header),
            $"the ascii header \"{header}\" (a wrap between words is fine)"
        );
        Assert.True(ShowsPhrase(scrollback, header), "the header must be present: " + scrollback);
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
        WriteSkill(host.Temp.File("home"), "greek");
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await host.WaitForScrollbackAsync("extensions 0 · MCP 0 · skills 1");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_counts_name_the_extensions_of_a_real_composition()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() }
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = await host.WaitForScrollbackAsync("extensions 1 · MCP 0 · skills 0");
        Assert.Equal(1, Occurrences(scrollback, Mark));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_builtin_does_not_count_as_an_installed_extension()
    {
        using var temp = new TempDirectory();
        using var host = new InteractiveSessionHost(
            environment: KeyedEnvironment(),
            extensions: new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            }
        );
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = await host.WaitForScrollbackAsync("skills 0");
        // roslyn composes; it is host-owned code, so the count of installed extensions stays 0.
        Assert.Contains("extensions 0 · MCP 0 · skills 0", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_first_run_names_auth_as_the_one_fix_and_no_counts_or_hint()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = await host.WaitForScrollbackAsync("run /auth to configure a provider");
        Assert.Equal(1, Occurrences(scrollback, "run /auth to configure a provider"));
        Assert.DoesNotContain("auth.json", scrollback, StringComparison.Ordinal);
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

        string scrollback = await host.WaitForScrollbackAsync("set OPENAI_API_KEY to a valid key");
        Assert.Equal(1, Occurrences(scrollback, "set OPENAI_API_KEY to a valid key"));
        // Exactly one actionable path: the named variable, never the auth store as well.
        Assert.DoesNotContain("auth.json", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_first_run_without_a_model_names_auth_and_no_counts_or_hint()
    {
        using var host = new InteractiveSessionHost(
            environment: PrintModeTestSupport.Environment(model: null)
        );
        Task run = host.RunAsync();

        // Nothing resolves without a model, so composition fails right after the header printed:
        // the run ends on its own and the header is the whole first frame.
        string scrollback = await host.ScrollbackAfterAsync(() => run);
        Assert.Equal(1, Occurrences(scrollback, "run /auth to configure a provider"));
        Assert.DoesNotContain("settings.json", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("extensions", scrollback, StringComparison.Ordinal);
        Assert.DoesNotContain("Esc cancels", scrollback, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(scrollback, Mark));
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
        // Nothing may print, so the /new notice is the event: read once the frame that carries it
        // completed, and assert the absence on that surface.
        Assert.DoesNotContain(
            Mark,
            await host.ScrollbackAfterAsync(() =>
                InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session")
            ),
            StringComparison.Ordinal
        );

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
        Assert.DoesNotContain(
            Mark,
            await host.ScrollbackAfterAsync(() =>
                InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session")
            ),
            StringComparison.Ordinal
        );

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

        // Two headers only exist once /new printed its own: wait for that, then count on one frame.
        string scrollback = await host.WaitForScrollbackAsync(
            text => Occurrences(text, Mark) == 2,
            "the second header /new prints"
        );
        // The first frame printed the placeholder once and got its one update line; the /new block
        // prints the counts directly, so no second placeholder and no second update line appears.
        Assert.Equal(1, Occurrences(scrollback, "extensions … · MCP … · skills …"));
        Assert.Equal(2, Occurrences(scrollback, "extensions 0 · MCP 0 · skills 0"));

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

    /// <summary>
    /// Whether the scrollback shows <paramref name="phrase"/> as a run of words, allowing the
    /// terminal's wrap between any two of them. A header longer than the scrollback width is one
    /// line in the application and two rows in the recording; asserting it as one substring makes
    /// a test fail the day the version string grows, which is not a product defect.
    /// </summary>
    private static bool ShowsPhrase(string scrollback, string phrase) =>
        Regex.IsMatch(
            scrollback,
            string.Join("\\s+", phrase.Split(' ').Select(Regex.Escape)),
            RegexOptions.CultureInvariant
        );

    /// <summary>A skill at <c>&lt;root&gt;/.agents/skills/&lt;name&gt;</c>: the user scope under a
    /// session's home, the project scope under the worktree.</summary>
    private static void WriteSkill(string root, string name)
    {
        string directory = Path.Combine(root, ".agents", "skills", name);
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
