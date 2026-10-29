using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The hard guard behind the context mask (issue 187, part 1): the read that carried a credential
/// left the model holding <c>[redacted:&lt;name&gt;]</c>, so a result the model writes back would
/// replace the real value with the placeholder and destroy the credential. A write over a file
/// that currently holds the value is refused; an edit carrying a placeholder on either side is
/// refused; the placeholder itself survives the persistence pass unchanged.
/// </summary>
public sealed class RedactionRefusalTests
{
    private const string Secret = "sk-ant-abcdef-123456";
    private const string Marker = "[redacted:ANTHROPIC_API_KEY]";
    private const string EnvText = $"ANTHROPIC_API_KEY={Secret}\nOTHER=1\n";
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // --- The write guard -------------------------------------------------

    [Fact]
    public async Task A_write_of_the_placeholder_over_the_real_value_is_refused_and_the_file_keeps_its_bytes()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new WriteTool(new Workspace(temp.Root), null, null, Redactor());
        byte[] before = Hash(EnvText);

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new { path = ".env", content = $"ANTHROPIC_API_KEY={Marker}\nOTHER=1\n" }
            ),
            Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains(Marker, result.Output, StringComparison.Ordinal);
        // Byte-identical, compared as a hash: the refused write is no write at all.
        Assert.Equal(before, HashOf(temp.File(".env")));
    }

    [Fact]
    public async Task A_write_into_a_file_that_does_not_hold_the_value_writes_normally()
    {
        using var temp = new TempDirectory();
        var tool = new WriteTool(new Workspace(temp.Root), null, null, Redactor());

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new { path = "notes.md", content = $"The redactor wrote {Marker} here.\n" }
            ),
            Context,
            Ct
        );

        Assert.False(result.IsError);
        Assert.Contains(Marker, File.ReadAllText(temp.File("notes.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_placeholder_the_session_does_not_know_writes_normally()
    {
        using var temp = new TempDirectory();
        var tool = new WriteTool(new Workspace(temp.Root), null, null, Redactor());

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new { path = "noted.txt", content = "THING=[redacted:SOMETHING_ELSE]\n" }
            ),
            Context,
            Ct
        );

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task A_write_is_unaffected_when_no_redactor_is_bound()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new WriteTool(new Workspace(temp.Root));

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new { path = ".env", content = $"ANTHROPIC_API_KEY={Marker}\nOTHER=1\n" }
            ),
            Context,
            Ct
        );

        Assert.False(result.IsError);
    }

    // --- The edit guard --------------------------------------------------

    [Fact]
    public async Task An_edit_carrying_the_placeholder_on_the_old_side_is_refused()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new EditTool(new Workspace(temp.Root), null, null, Redactor());
        byte[] before = Hash(EnvText);

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    path = ".env",
                    old_text = $"ANTHROPIC_API_KEY={Marker}",
                    new_text = "ANTHROPIC_API_KEY=new-plain-value",
                }
            ),
            Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains(Marker, result.Output, StringComparison.Ordinal);
        Assert.Equal(before, HashOf(temp.File(".env")));
    }

    [Fact]
    public async Task An_edit_carrying_the_placeholder_on_the_new_side_is_refused()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new EditTool(new Workspace(temp.Root), null, null, Redactor());
        byte[] before = Hash(EnvText);

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    path = ".env",
                    old_text = "OTHER=1",
                    new_text = $"OTHER=2 # copied from {Marker}",
                }
            ),
            Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Equal(before, HashOf(temp.File(".env")));
    }

    [Fact]
    public async Task An_edit_with_a_placeholder_the_session_does_not_know_is_refused_too()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new EditTool(new Workspace(temp.Root), null, null, Redactor());
        byte[] before = Hash(EnvText);

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    path = ".env",
                    old_text = "OTHER=1",
                    new_text = "OTHER=2 [redacted:SOMETHING_ELSE]",
                }
            ),
            Context,
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains(
            "edit around the credential line instead of through it",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(before, HashOf(temp.File(".env")));
    }

    [Fact]
    public async Task An_edit_is_unaffected_when_no_redactor_is_bound()
    {
        using var temp = new TempDirectory();
        WriteEnv(temp);
        var tool = new EditTool(new Workspace(temp.Root));

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    path = ".env",
                    old_text = "OTHER=1",
                    new_text = $"OTHER={Marker}",
                }
            ),
            Context,
            Ct
        );

        Assert.False(result.IsError);
    }

    // --- The placeholder's own round trip --------------------------------

    [Fact]
    public void The_placeholder_survives_the_persistence_pass_unchanged()
    {
        string text = $"KEY={Marker}\nTOK=[redacted:openai]\nUNMAPPED=[redacted:SOMETHING_ELSE]\n";
        CredentialRedactor persistence = Redactor();

        Assert.Equal(text, persistence.Redact(text), StringComparer.Ordinal);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(
            Encoding.UTF8.GetString(bytes),
            Encoding.UTF8.GetString(persistence.RedactUtf8(bytes))
        );
    }

    // --- Helpers ---------------------------------------------------------

    private static CredentialRedactor Redactor() =>
        new([("ANTHROPIC_API_KEY", Secret), ("openai", "sk-openai-123456")]);

    private static void WriteEnv(TempDirectory temp) =>
        File.WriteAllText(temp.File(".env"), EnvText);

    private static byte[] Hash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    private static byte[] HashOf(string path) => SHA256.HashData(File.ReadAllBytes(path));
}
