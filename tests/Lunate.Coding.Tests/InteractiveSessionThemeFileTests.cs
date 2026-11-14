using System.Text.RegularExpressions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The user theme file in a running session: <c>theme.json</c> in the session's home (the store
/// <c>$LUNATE_HOME</c> moves) overlays the selected built-in, <c>--theme &lt;path&gt;</c> reads a
/// file of its own and the flag wins, and a file that cannot be applied is discarded whole with one
/// warning line while the built-in theme stands.
/// </summary>
[Collection(InteractiveSessionTuiSettingsCollection.Name)]
public sealed class InteractiveSessionThemeFileTests
{
    [Fact]
    public async Task A_theme_file_under_the_home_recolours_the_live_frame()
    {
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            environment: TrueColorEnvironment
        );
        WriteHomeTheme(host, """{"prompt":"#ff0000"}""");

        Task run = host.RunAsync();
        await host.WaitUntilAsync(Painted(host, "\u001b[38;2;255;0;0m"));

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task The_theme_flag_path_wins_over_the_home_file()
    {
        string flagPath = ScratchTheme("""{"prompt":"#00ff00"}""");
        try
        {
            using var host = new InteractiveSessionHost(
                interactiveConsole: true,
                environment: TrueColorEnvironment,
                launch: new LaunchOptions { Theme = flagPath }
            );
            WriteHomeTheme(host, """{"prompt":"#ff0000"}""");

            Task run = host.RunAsync();
            await host.WaitUntilAsync(Painted(host, "\u001b[38;2;0;255;0m"));

            Assert.DoesNotContain("\u001b[38;2;255;0;0m", host.Frames, StringComparison.Ordinal);
            await QuitAsync(host, run);
        }
        finally
        {
            File.Delete(flagPath);
        }
    }

    [Fact]
    public async Task The_tui_theme_setting_paints_at_startup()
    {
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            environment: TrueColorEnvironment
        );
        WriteSettings(host, """{"schemaVersion": 2, "tui": {"theme": "mono"}}""");

        Task run = host.RunAsync();

        // The setting is the selection when no flag names one, so the repaint after the theme is
        // resolved carries no colour attribute: the default theme's prompt tone is gone.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> Ask anything", StringComparison.Ordinal)
                && !Regex.IsMatch(host.LastFrame, @"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);
        });

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task The_theme_flag_name_wins_over_the_tui_theme_setting()
    {
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            environment: TrueColorEnvironment,
            launch: new LaunchOptions { Theme = "ansi16" }
        );
        WriteSettings(host, """{"schemaVersion": 2, "tui": {"theme": "mono"}}""");

        Task run = host.RunAsync();

        // ansi16 pins the sixteen-colour system over the setting's colourless frame, so the prompt
        // marker paints ANSI blue — the flag decides the selection before the setting is read.
        await host.WaitUntilAsync(Painted(host, "\u001b[34m"));
        Assert.DoesNotContain("\u001b[38;2", host.LastFrame, StringComparison.Ordinal);

        await QuitAsync(host, run);
    }

    [Fact]
    public async Task A_theme_flag_path_keeps_the_tui_theme_setting_as_its_base()
    {
        string flagPath = ScratchTheme("""{"prompt":"colour:208"}""");
        try
        {
            using var host = new InteractiveSessionHost(
                interactiveConsole: true,
                environment: TrueColorEnvironment,
                launch: new LaunchOptions { Theme = flagPath }
            );
            WriteSettings(host, """{"schemaVersion": 2, "tui": {"theme": "ansi16"}}""");

            Task run = host.RunAsync();

            // The flag names a file, not a built-in, so the setting is still the base theme and
            // pins the sixteen-colour plan: the file's 256 index paints the nearest of the sixteen
            // (bright yellow) rather than the exact entry a true-colour base would emit.
            await host.WaitUntilAsync(Painted(host, "\u001b[93m"));
            Assert.DoesNotContain("\u001b[38;5;208m", host.LastFrame, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b[38;2", host.LastFrame, StringComparison.Ordinal);

            await QuitAsync(host, run);
        }
        finally
        {
            File.Delete(flagPath);
        }
    }

    [Fact]
    public async Task An_unknown_token_warns_and_the_built_in_theme_stands()
    {
        using var host = new InteractiveSessionHost(
            interactiveConsole: true,
            environment: TrueColorEnvironment
        );
        WriteHomeTheme(host, """{"highlight":"red"}""");

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("token 'highlight'", StringComparison.Ordinal);
        });

        // The default prompt tone is still what paints: the file did not half-apply.
        await host.WaitUntilAsync(Painted(host, "\u001b[38;2;135;175;215m"));

        await QuitAsync(host, run);
    }

    /// <summary>Advances the scheduler until the live frame carries <paramref name="sgr"/>.</summary>
    private static Func<bool> Painted(InteractiveSessionHost host, string sgr) =>
        () =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(sgr, StringComparison.Ordinal);
        };

    private static async Task QuitAsync(InteractiveSessionHost host, Task run)
    {
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static string? TrueColorEnvironment(string name) =>
        name switch
        {
            "COLORTERM" => "truecolor",
            "OPENAI_API_KEY" => "test-key",
            _ => PrintModeTestSupport.Environment()(name),
        };

    /// <summary>The session home's own theme file: <c>&lt;home&gt;/.lunate/theme.json</c>.</summary>
    private static void WriteHomeTheme(InteractiveSessionHost host, string json)
    {
        string store = System.IO.Path.Combine(host.Temp.File("home"), ".lunate");
        Directory.CreateDirectory(store);
        File.WriteAllText(System.IO.Path.Combine(store, "theme.json"), json);
    }

    private static void WriteSettings(InteractiveSessionHost host, string json) =>
        File.WriteAllText(host.Temp.File("settings.json"), json);

    /// <summary>A scratch token file under the process temp root (<c>$TMPDIR</c> on POSIX).</summary>
    private static string ScratchTheme(string json)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"lunate-theme-{Guid.NewGuid():N}.json"
            )
        );
        File.WriteAllText(path, json);
        return path;
    }
}
