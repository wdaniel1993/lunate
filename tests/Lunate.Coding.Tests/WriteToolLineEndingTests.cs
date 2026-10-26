using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The write tool's half of the line-ending contract (AGENTS.md, Rules): an existing file keeps its
/// dominant ending and its byte-order mark, so a rewrite never corrupts the user's bytes; a new file
/// follows <c>.editorconfig</c>, then <c>.gitattributes</c>, else the platform default, so a file
/// created here matches the project it lands in.
/// </summary>
public sealed class WriteToolLineEndingTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public async Task An_existing_CRLF_file_stays_CRLF()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Windows.txt");
        File.WriteAllText(path, "one\r\ntwo\r\n");

        var result = await WriteAsync(temp, "Windows.txt", "one\ntwo\nthree\n");

        Assert.False(result.IsError);
        Assert.Equal("one\r\ntwo\r\nthree\r\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task An_existing_byte_order_mark_survives_a_rewrite()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Marked.cs");
        File.WriteAllBytes(path, [.. Bom, .. Encoding.UTF8.GetBytes("old\n")]);

        var result = await WriteAsync(temp, "Marked.cs", "new\n");

        Assert.False(result.IsError);
        Assert.Equal([.. Bom, .. Encoding.UTF8.GetBytes("new\n")], File.ReadAllBytes(path));
    }

    [Fact]
    public async Task An_existing_CRLF_file_with_a_BOM_keeps_both()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Both.cs");
        File.WriteAllBytes(path, [.. Bom, .. Encoding.UTF8.GetBytes("a\r\nb\r\n")]);

        var result = await WriteAsync(temp, "Both.cs", "a\nb\nc\n");

        Assert.False(result.IsError);
        Assert.Equal(
            [.. Bom, .. Encoding.UTF8.GetBytes("a\r\nb\r\nc\r\n")],
            File.ReadAllBytes(path)
        );
    }

    [Fact]
    public async Task A_new_file_follows_an_editorconfig_that_asks_for_LF()
    {
        using var temp = new TempDirectory();
        WriteEditorConfig(temp, "[*]\nend_of_line = lf\n");

        var result = await WriteAsync(temp, "Notes.txt", "one\r\ntwo\r\n");

        Assert.False(result.IsError);
        Assert.Equal("one\ntwo\n", File.ReadAllText(temp.File("Notes.txt")));
    }

    [Fact]
    public async Task A_new_file_follows_an_editorconfig_that_asks_for_CRLF()
    {
        using var temp = new TempDirectory();
        WriteEditorConfig(temp, "[*]\nend_of_line = crlf\n");

        var result = await WriteAsync(temp, "Notes.txt", "one\ntwo\n");

        Assert.False(result.IsError);
        Assert.Equal("one\r\ntwo\r\n", File.ReadAllText(temp.File("Notes.txt")));
    }

    [Fact]
    public async Task A_nested_directory_inherits_the_editorconfig_above_it()
    {
        using var temp = new TempDirectory();
        WriteEditorConfig(temp, "[*]\nend_of_line = crlf\n");

        var result = await WriteAsync(temp, "nested/deeper/Notes.txt", "one\n");

        Assert.False(result.IsError);
        Assert.Equal("one\r\n", File.ReadAllText(temp.File("nested/deeper/Notes.txt")));
    }

    [Fact]
    public async Task A_new_file_follows_an_editorconfig_charset_of_utf_8_bom()
    {
        using var temp = new TempDirectory();
        WriteEditorConfig(temp, "[*]\nend_of_line = lf\ncharset = utf-8-bom\n");

        var result = await WriteAsync(temp, "Marked.cs", "x\n");

        Assert.False(result.IsError);
        Assert.Equal(
            [.. Bom, .. Encoding.UTF8.GetBytes("x\n")],
            File.ReadAllBytes(temp.File("Marked.cs"))
        );
    }

    [Fact]
    public async Task Editorconfig_wins_over_gitattributes()
    {
        using var temp = new TempDirectory();
        WriteEditorConfig(temp, "[*]\nend_of_line = crlf\n");
        File.WriteAllText(temp.File(".gitattributes"), "*.txt text eol=lf\n");

        var result = await WriteAsync(temp, "Notes.txt", "one\n");

        Assert.False(result.IsError);
        Assert.Equal("one\r\n", File.ReadAllText(temp.File("Notes.txt")));
    }

    [Fact]
    public async Task A_new_file_follows_gitattributes_when_no_editorconfig_applies()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".gitattributes"), "*.txt text eol=crlf\n");

        var result = await WriteAsync(temp, "Notes.txt", "one\ntwo\n");

        Assert.False(result.IsError);
        Assert.Equal("one\r\ntwo\r\n", File.ReadAllText(temp.File("Notes.txt")));
    }

    [Fact]
    public async Task A_gitattributes_rule_for_another_type_does_not_apply()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File(".gitattributes"), "*.txt text eol=crlf\n");

        var result = await WriteAsync(temp, "Notes.md", "one\n");

        Assert.False(result.IsError);
        Assert.Equal(
            "one\n".ReplaceLineEndings(Environment.NewLine),
            File.ReadAllText(temp.File("Notes.md"))
        );
    }

    [Fact]
    public async Task A_new_file_without_a_policy_follows_the_platform_default()
    {
        using var temp = new TempDirectory();

        var result = await WriteAsync(temp, "Plain.txt", "one\ntwo\n");

        Assert.False(result.IsError);
        Assert.Equal(
            "one\ntwo\n".ReplaceLineEndings(Environment.NewLine),
            File.ReadAllText(temp.File("Plain.txt"))
        );
    }

    private static void WriteEditorConfig(TempDirectory temp, string sections) =>
        File.WriteAllText(temp.File(".editorconfig"), "root = true\n" + sections);

    private static Task<ToolResult> WriteAsync(TempDirectory temp, string path, string content) =>
        new WriteTool(new Workspace(temp.Root)).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { path, content }),
            Context,
            CancellationToken.None
        );
}
