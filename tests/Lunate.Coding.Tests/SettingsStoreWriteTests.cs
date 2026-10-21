using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The settings writer: read-modify-write over the existing JSON, schema 2 on every write, and an
/// atomic replacement that leaves the old file when anything fails (design D7).
/// </summary>
public sealed class SettingsStoreWriteTests
{
    [Fact]
    public void A_write_preserves_unrelated_keys_and_emits_schema_2()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "model": "gpt-4o-mini",
              "approval": "auto-edit",
              "output": { "toolResultLimit": 1234 },
              "thinking": "high",
              "startupHeader": false
            }
            """
        );

        SettingsStore.WriteFavorites(["gpt-4o-mini", "claude-sonnet-5-5"], path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            ["gpt-4o-mini", "claude-sonnet-5-5"],
            root.GetProperty("favorites").EnumerateArray().Select(item => item.GetString()!)
        );
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal("auto-edit", root.GetProperty("approval").GetString());
        Assert.Equal(1234, root.GetProperty("output").GetProperty("toolResultLimit").GetInt32());
        Assert.Equal("high", root.GetProperty("thinking").GetString());
        Assert.False(root.GetProperty("startupHeader").GetBoolean());

        AgentSettings resolved = SettingsStore.Resolve(path, _ => null);
        Assert.Equal(["gpt-4o-mini", "claude-sonnet-5-5"], resolved.Favorites);
        Assert.Equal(ApprovalPolicy.AutoEdit, resolved.Approval);
    }

    [Fact]
    public void A_write_creates_a_missing_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("nested/settings.json");

        SettingsStore.WriteFavorites(["gpt-4o-mini"], path);

        AgentSettings settings = SettingsStore.Resolve(path, _ => null);
        Assert.Equal(["gpt-4o-mini"], settings.Favorites);
    }

    [Theory]
    [InlineData("gpt-4o-mini", "gpt-4o-mini")]
    [InlineData("gpt-4o-mini", "")]
    public void A_write_keeps_the_old_file_when_validation_fails(params string[] favorites)
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        string original = """{ "schemaVersion": 1, "model": "gpt-4o" }""";
        File.WriteAllText(path, original);

        Assert.Throws<InvalidDataException>(() => SettingsStore.WriteFavorites(favorites, path));

        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void A_write_trims_every_favourite()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");

        SettingsStore.WriteFavorites([" gpt-4o ", "\tclaude-sonnet-5-5\n"], path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            ["gpt-4o", "claude-sonnet-5-5"],
            document
                .RootElement.GetProperty("favorites")
                .EnumerateArray()
                .Select(item => item.GetString()!)
        );
        Assert.Equal(
            ["gpt-4o", "claude-sonnet-5-5"],
            SettingsStore.Resolve(path, _ => null).Favorites
        );
    }

    [Fact]
    public void A_write_rejects_a_whitespace_only_id_and_keeps_the_old_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        string original = """{ "schemaVersion": 1, "model": "gpt-4o" }""";
        File.WriteAllText(path, original);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.WriteFavorites(["gpt-4o-mini", "  "], path)
        );

        Assert.Contains("non-empty model ids", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void A_duplicate_key_settings_file_is_reported_and_left_byte_identical()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        string original = """
            {
              "schemaVersion": 2,
              "schemaVersion": 2,
              "model": "gpt-4o"
            }
            """;
        File.WriteAllText(path, original);
        byte[] before = File.ReadAllBytes(path);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.WriteFavorites(["gpt-4o-mini"], path)
        );

        Assert.Contains("not valid JSON", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were written", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_write_rejects_more_than_eight_favourites_and_keeps_the_old_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        string original = """{ "schemaVersion": 1 }""";
        File.WriteAllText(path, original);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.WriteFavorites(["a", "b", "c", "d", "e", "f", "g", "h", "i"], path)
        );

        Assert.Contains("8 entries", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void A_write_rejects_an_invalid_existing_file_and_leaves_it_untouched()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        string original = """{ "schemaVersion": 2, "unknown": true }""";
        File.WriteAllText(path, original);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.WriteFavorites(["gpt-4o-mini"], path)
        );

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were written", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void A_failed_replacement_leaves_the_old_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string directory = temp.File("readonly");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        string original = """{ "schemaVersion": 1, "model": "gpt-4o" }""";
        File.WriteAllText(path, original);
        UnixFileMode mode = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Exception? failure = Record.Exception(() =>
                SettingsStore.WriteFavorites(["gpt-4o-mini"], path)
            );
            Assert.True(
                failure is IOException or UnauthorizedAccessException,
                $"expected a write failure, got {failure?.GetType().Name ?? "none"}"
            );
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }
    }

    [Fact]
    public void A_replacement_preserves_the_existing_posix_mode_and_leaves_no_temp_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1 }""");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        SettingsStore.WriteFavorites(["gpt-4o-mini"], path);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }
}
