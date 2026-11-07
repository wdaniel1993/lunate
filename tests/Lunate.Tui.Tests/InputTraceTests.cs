namespace Lunate.Tui.Tests;

/// <summary>
/// The boundary of the <c>LUNATE_INPUT_TRACE</c> trace: characters and pastes are reduced to
/// counts, the file is written only where the state root allows it, and a value that escapes that
/// root is refused instead of honored.
/// </summary>
public sealed class InputTraceTests
{
    private const string Secret = "sk-groq-secret-12345";

    [Fact]
    public void An_unset_or_blank_setting_traces_nowhere()
    {
        Assert.Null(InputTrace.Resolve(null, "/tmp/some-root"));
        Assert.Null(InputTrace.Resolve("", "/tmp/some-root"));
        Assert.Null(InputTrace.Resolve("   ", "/tmp/some-root"));
    }

    [Fact]
    public void The_enabled_value_takes_the_default_file_under_the_state_root()
    {
        Assert.Equal(
            Path.Combine("/somewhere", "diagnostics", "input-trace.log"),
            InputTrace.Resolve(InputTrace.EnabledValue, "/somewhere")
        );
    }

    [Fact]
    public void A_relative_value_resolves_against_the_state_root()
    {
        var temp = new TempDirectory();
        try
        {
            string resolved = InputTrace.Resolve("diagnostics/my-trace.log", temp.Root)!;

            Assert.Equal(Path.Combine(temp.Root, "diagnostics", "my-trace.log"), resolved);
        }
        finally
        {
            temp.Dispose();
        }
    }

    [Fact]
    public void A_value_escaping_the_state_root_is_refused()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows has no /tmp and no UnixFileMode coverage below; POSIX-only facts.
            return;
        }

        using var temp = new TempDirectory();
        string outside = temp.File("outside.log");

        Assert.Null(InputTrace.Resolve(outside, Path.Combine(temp.Root, "state")));
        Assert.Null(InputTrace.Resolve("../outside.log", Path.Combine(temp.Root, "state")));
    }

    [Fact]
    public void A_path_inside_the_state_root_survives_a_link()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string inner = temp.File("inner");
        Directory.CreateDirectory(inner);
        string link = temp.File("link");
        File.CreateSymbolicLink(link, inner);

        Assert.Equal(
            Path.Combine(link, "trace.log"),
            InputTrace.Resolve(Path.Combine(link, "trace.log"), temp.Root)
        );
    }

    [Fact]
    public void A_credential_is_recorded_as_a_count_not_as_text()
    {
        using var temp = new TempDirectory();
        string file = temp.File("trace.log");

        InputTrace.Key(new KeyEvent(KeyKind.Character, Secret, false, false, false), file);
        InputTrace.Key(new KeyEvent(KeyKind.Paste, Secret + "\n", false, false, false), file);
        InputTrace.Key(new KeyEvent(KeyKind.Escape, null, false, false, false), file);

        string content = File.ReadAllText(file);
        Assert.DoesNotContain(Secret, content, StringComparison.Ordinal);
        Assert.DoesNotContain("groq", content, StringComparison.Ordinal);
        Assert.Contains($"chars={Secret.Length}", content, StringComparison.Ordinal);
        Assert.Contains($"chars={Secret.Length + 1}", content, StringComparison.Ordinal);
        Assert.Contains("kind=Escape", content, StringComparison.Ordinal);
        Assert.Contains("chars=-", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trace_file_is_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string file = temp.File("trace.log");

        InputTrace.Write(file, "line");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(file)
                & ~(UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit)
        );
    }

    [Fact]
    public void The_trace_file_is_UTF8_without_BOM_and_LF_ended()
    {
        using var temp = new TempDirectory();
        string file = temp.File("trace.log");

        InputTrace.Write(file, "line");
        InputTrace.Write(file, "other");

        byte[] bytes = File.ReadAllBytes(file);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.EndsWith("line\nother\n", File.ReadAllText(file), StringComparison.Ordinal);
    }
}
