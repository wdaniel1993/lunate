using System.Text;

namespace Lunate.Coding.Tests;

/// <summary>
/// The editor contract without a terminal: which program wins, how a value splits, where a name is
/// found, and what the draft file looks like on disk. No child process runs here.
/// </summary>
public sealed class DraftEditorTests
{
    [Fact]
    public void Visual_wins_over_editor_and_carries_its_arguments()
    {
        EditorCommand command = DraftEditor.Resolve(
            Env(("VISUAL", "vim -u NONE"), ("EDITOR", "nano")),
            windows: false
        );

        Assert.Equal("vim", command.Program);
        Assert.Equal(["-u", "NONE"], command.Arguments);
        Assert.Equal("VISUAL", command.Source);
        Assert.Equal("vim -u NONE", command.Value);
        Assert.Equal("VISUAL=\"vim -u NONE\"", command.Label);
    }

    [Fact]
    public void A_blank_variable_falls_through_to_the_next_candidate()
    {
        EditorCommand command = DraftEditor.Resolve(
            Env(("VISUAL", "   "), ("EDITOR", "nano -w")),
            windows: false
        );

        Assert.Equal("nano", command.Program);
        Assert.Equal(["-w"], command.Arguments);
        Assert.Equal("EDITOR", command.Source);
    }

    [Fact]
    public void Neither_variable_falls_back_to_the_platform_default()
    {
        EditorCommand posix = DraftEditor.Resolve(Env(), windows: false);
        EditorCommand windows = DraftEditor.Resolve(Env(), windows: true);

        Assert.Equal("vi", posix.Program);
        Assert.Empty(posix.Arguments);
        Assert.Null(posix.Source);
        Assert.Equal("vi", posix.Label);

        Assert.Equal("notepad", windows.Program);
        Assert.Null(windows.Source);
    }

    [Fact]
    public void A_value_is_split_on_whitespace_with_no_shell_and_no_quotes()
    {
        // One rule everywhere: the first token is the program, the rest are arguments. A quoted
        // path with a space does not survive, which is the documented cost of having no shell.
        EditorCommand command = DraftEditor.Resolve(
            Env(("EDITOR", "\"/path with space/vim\" -u NONE")),
            windows: false
        );

        Assert.Equal("\"/path", command.Program);
        Assert.Equal(["with", "space/vim\"", "-u", "NONE"], command.Arguments);
    }

    [Theory]
    [InlineData("line one\r\nline two\r\n", "line one\nline two")]
    [InlineData("line one\nline two\n\n", "line one\nline two\n")]
    [InlineData("no trailing newline", "no trailing newline")]
    [InlineData("", "")]
    [InlineData("\n", "")]
    public void Read_back_normalizes_and_drops_exactly_one_trailing_newline(
        string edited,
        string expected
    ) => Assert.Equal(expected, DraftEditor.ReadBack(edited));

    [Fact]
    public void The_draft_file_is_utf8_without_a_bom_and_owner_only()
    {
        using var temp = new TempDirectory();
        string path = temp.File("lunate-draft-test");

        DraftEditor.WriteDraft(path, "draft \u00e9\u2713");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "the draft file must not carry a UTF-8 BOM"
        );
        Assert.Equal("draft \u00e9\u2713", Encoding.UTF8.GetString(bytes));

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [Fact]
    public void A_program_is_found_on_the_path_and_a_missing_one_is_reported()
    {
        using var temp = new TempDirectory();
        string program = temp.File("my-editor");
        File.WriteAllText(program, string.Empty);

        Assert.Equal(
            program,
            DraftEditor.Locate("my-editor", Env(("PATH", temp.Root)), windows: false)
        );
        Assert.Null(
            DraftEditor.Locate("no-such-editor-anywhere", Env(("PATH", temp.Root)), windows: false)
        );
        Assert.Null(DraftEditor.Locate("my-editor", Env(("PATH", null)), windows: false));
    }

    [Fact]
    public void An_absolute_program_path_is_used_as_given()
    {
        using var temp = new TempDirectory();
        string program = temp.File("direct-editor");
        File.WriteAllText(program, string.Empty);

        Assert.Equal(
            program,
            DraftEditor.Locate(program, Env(("PATH", temp.Root)), windows: false)
        );
        Assert.Null(
            DraftEditor.Locate(temp.File("absent"), Env(("PATH", temp.Root)), windows: false)
        );
    }

    [Fact]
    public void The_failure_notices_name_the_variable_the_exit_code_and_the_read()
    {
        var command = new EditorCommand("nvim-preview", [], "VISUAL", "nvim-preview");

        Assert.Equal(
            "VISUAL=\"nvim-preview\": no such program \u2014 draft unchanged",
            DraftEditor.MissingProgramNotice(command)
        );
        Assert.Equal("editor exited 1 \u2014 draft unchanged", DraftEditor.ExitNotice(1));
        Assert.Equal(
            "could not start vi: permission denied",
            DraftEditor.StartFailureNotice(
                new EditorCommand("vi", [], null, "vi"),
                new System.ComponentModel.Win32Exception("permission denied")
            )
        );
        Assert.Equal(
            "could not read the edited draft: the file is gone",
            DraftEditor.ReadFailureNotice(new IOException("the file is gone"))
        );
    }

    /// <summary>The environment seam: a dictionary lookup, exactly like the session's.</summary>
    private static Func<string, string?> Env(params (string Name, string? Value)[] values)
    {
        var map = values.ToDictionary(
            entry => entry.Name,
            entry => entry.Value,
            StringComparer.Ordinal
        );
        return name => map.TryGetValue(name, out string? value) ? value : null;
    }
}
