using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The settings file's <c>tui</c> section: the named theme and the persisted key remaps. The
/// reader validates with the same parsers the <c>/settings</c> edit uses, the writer is a
/// read-modify-write (unrelated keys survive, the file is replaced atomically at schema 2), and a
/// refused write leaves the old file byte for byte.
/// </summary>
public sealed class SettingsStoreTuiTests
{
    [Fact]
    public void A_missing_tui_section_yields_the_defaults()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{ "schemaVersion": 1 }""");

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal("default", settings.Tui.Theme);
        Assert.Empty(settings.Tui.Keys);
    }

    [Fact]
    public void A_tui_section_resolves_the_theme_and_the_remaps()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tui": { "theme": "mono", "keys": { "ctrl-o.transcript": ["Ctrl+T"] } }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal("mono", settings.Tui.Theme);
        Assert.Equal(["Ctrl+T"], settings.Tui.Keys["ctrl-o.transcript"]);
    }

    [Theory]
    [InlineData("""{ "theme": "banana" }""", "'tui.theme' must be 'default', 'ansi16' or 'mono'")]
    [InlineData("""{ "theme": 4 }""", "'tui.theme' must be 'default', 'ansi16' or 'mono'")]
    [InlineData("""{ "keys": { "ctrl-newline-who": ["Ctrl+T"] } }""", "is not a binding id")]
    [InlineData("""{ "keys": { "ctrl-o.transcript": ["banana"] } }""", "declares 'banana'")]
    [InlineData(
        """{ "keys": { "ctrl-o.transcript": [] } }""",
        "'tui.keys.ctrl-o.transcript' must be a non-empty array of key specs"
    )]
    [InlineData(
        """{ "keys": { "ctrl-o.transcript": "Ctrl+T" } }""",
        "'tui.keys.ctrl-o.transcript' must be an array of key specs"
    )]
    [InlineData("""{ "colour": "mono" }""", "unknown key 'tui.colour'")]
    public void An_invalid_tui_value_is_refused_with_what_to_do_next(string tui, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""{ "schemaVersion": 2, "tui": {{tui}} }"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_remapped_binding_is_refused_with_the_nearest_ids_named()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tui": { "keys": { "ctrl-o.transcript-not-real": ["Ctrl+T"] } }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("the closest names are", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'ctrl-o.transcript'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTui_preserves_unrelated_settings_and_round_trips_through_the_reader()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "thinking": "high",
              "output": { "toolResultLimit": 1234 }
            }
            """
        );

        SettingsStore.WriteTui(
            new TuiSettings
            {
                Theme = "ansi16",
                Keys = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["ctrl-o.transcript"] = ["ctrl+t"],
                },
            },
            path
        );

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());

        AgentSettings resolved = SettingsStore.Resolve(path, _ => null);
        Assert.Equal("ansi16", resolved.Tui.Theme);
        // The saved spelling is the help overlay's, so a reader below sees what a guide reader
        // saw when the remap was written.
        Assert.Equal(["Ctrl+T"], resolved.Tui.Keys["ctrl-o.transcript"]);
        Assert.Equal(ThinkingLevel.High, resolved.Thinking);
        Assert.Equal(1234, resolved.ToolOutputLimit);
    }

    [Fact]
    public void WriteTui_on_a_missing_file_creates_a_schema_2_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");

        SettingsStore.WriteTui(new TuiSettings { Theme = "mono" }, path);

        Assert.Equal("mono", SettingsStore.Resolve(path, _ => null).Tui.Theme);
    }

    [Fact]
    public void A_write_that_the_reader_refuses_leaves_the_old_file_untouched()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        const string original = """{ "schemaVersion": 2, "thinking": "high" }""";
        File.WriteAllText(path, original);
        byte[] before = File.ReadAllBytes(path);

        TuiSettings badTheme = new() { Theme = "neon" };
        TuiSettings badKey = new()
        {
            Keys = new Dictionary<string, IReadOnlyList<string>>
            {
                ["not-a-binding-id"] = ["Ctrl+T"],
            },
        };

        foreach (TuiSettings bad in new[] { badTheme, badKey })
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                SettingsStore.WriteTui(bad, path)
            );

            Assert.Contains("Invalid settings in", exception.Message, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        Assert.Equal(original, File.ReadAllText(path));
    }
}
