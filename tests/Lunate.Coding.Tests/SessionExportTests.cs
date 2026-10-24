using System.Text;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The export forms (design decisions 4, 5 and 8): where a default export goes, how a path argument
/// overrides it, and that the JSONL form is the session file byte for byte with only the credential
/// redaction applied.
/// </summary>
public sealed class SessionExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Jsonl_preserves_every_entry_byte_identically_except_the_redacted_value()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp, "the token is sk-openai-123456 in the output");
        string source = File.ReadAllText(session.Path);
        string destination = temp.File("out.jsonl");
        CredentialRedactor redactor = new([("openai", "sk-openai-123456")]);

        ExportOutcome outcome = SessionExporter.Write(
            session,
            new ExportRequest(ExportFormat.Jsonl, destination, Force: false),
            destination,
            "title",
            redactor
        );

        string written = File.ReadAllText(destination);
        Assert.Equal(source.Replace("sk-openai-123456", "[redacted:openai]"), written);
        Assert.Equal(destination, outcome.Path);
        Assert.Equal(session.Entries.Count, outcome.Entries);
        Assert.DoesNotContain("sk-openai-123456", written, StringComparison.Ordinal);
        // Byte-identical apart from the marker: the file's own lines are otherwise untouched.
        byte[] original = Encoding.UTF8.GetBytes(
            source.Replace("sk-openai-123456", "[redacted:openai]")
        );
        Assert.Equal(original, File.ReadAllBytes(destination));
    }

    [Fact]
    public void The_default_destination_is_the_exports_directory_with_the_date_and_name()
    {
        string path = ExportTargets.Resolve(
            new ExportRequest(ExportFormat.Html, null, Force: false),
            homeDirectory: "/Users/ada",
            workingDirectory: "/repo",
            sessionNameOrId: "parser-refactor",
            Now
        );

        Assert.Equal("/Users/ada/.lunate/exports/2026-10-24-parser-refactor.html", path);
    }

    [Fact]
    public void A_jsonl_default_carries_the_jsonl_extension()
    {
        Assert.Equal(
            "/Users/ada/.lunate/exports/2026-10-24-s_20261024-093000-abcd.jsonl",
            ExportTargets.Resolve(
                new ExportRequest(ExportFormat.Jsonl, null, Force: false),
                "/Users/ada",
                "/repo",
                "s_20261024-093000-abcd",
                Now
            )
        );
    }

    [Fact]
    public void A_path_argument_overrides_the_default_and_is_made_absolute()
    {
        Assert.Equal(
            "/repo/reports/session.html",
            ExportTargets.Resolve(
                new ExportRequest(ExportFormat.Html, "reports/session.html", Force: false),
                "/Users/ada",
                "/repo",
                "parser-refactor",
                Now
            )
        );
        Assert.Equal(
            "/tmp/session.html",
            ExportTargets.Resolve(
                new ExportRequest(ExportFormat.Html, "/tmp/session.html", Force: false),
                "/Users/ada",
                "/repo",
                "parser-refactor",
                Now
            )
        );
    }

    [Fact]
    public void The_session_name_becomes_a_safe_file_fragment()
    {
        Assert.Equal("Parser-Refactor-v2", ExportTargets.FileName("Parser Refactor: v2"));
        Assert.Equal("a-b", ExportTargets.FileName("  a /// b  "));
        Assert.Equal("session", ExportTargets.FileName("///"));
        Assert.True(
            ExportTargets.FileName(new string('x', 200)).Length <= ExportTargets.MaxNameLength
        );
    }

    [Fact]
    public void The_export_inherits_the_session_files_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        Session session = BuildSession(temp, "answer");
        File.SetUnixFileMode(session.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string destination = temp.File("out.html");

        SessionExporter.Write(
            session,
            new ExportRequest(ExportFormat.Html, destination, Force: false),
            destination,
            "title",
            CredentialRedactor.Empty
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(destination)
        );
    }

    [Fact]
    public void The_export_command_parses_the_form_and_the_force_flag()
    {
        Assert.True(ExportRequest.TryParse("html", out ExportRequest html, out _));
        Assert.Equal(ExportFormat.Html, html.Format);
        Assert.Null(html.Path);
        Assert.False(html.Force);

        Assert.True(
            ExportRequest.TryParse("jsonl --force reports/x.jsonl", out ExportRequest forced, out _)
        );
        Assert.Equal(ExportFormat.Jsonl, forced.Format);
        Assert.Equal("reports/x.jsonl", forced.Path);
        Assert.True(forced.Force);

        Assert.False(ExportRequest.TryParse("", out _, out string empty));
        Assert.Equal(ExportRequest.Usage, empty);
        Assert.False(ExportRequest.TryParse("html a b", out _, out _));
        Assert.False(ExportRequest.TryParse("markdown", out _, out _));
    }

    /// <summary>A three-entry session with one assistant answer carrying the given tool output.</summary>
    private static Session BuildSession(TempDirectory temp, string toolOutput)
    {
        Session session = Session.Create(temp.File("s_20261024-093000-abcd.jsonl"), temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "please run it"));
        session.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", toolOutput)]),
            "gpt-4o-mini"
        );
        session.AppendMessage(
            new ChatMessage(ChatRole.Assistant, "done"),
            "gpt-4o-mini",
            new SessionUsage(10, 4)
        );
        return session;
    }
}
