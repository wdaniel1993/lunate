using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The HTML export (design decisions 5 and 7): one self-contained document, thinking collapsed,
/// diffs classed, and every value from the session escaped into text — hostile tool output included.
/// </summary>
public sealed class SessionHtmlWriterTests
{
    [Fact]
    public void The_document_carries_the_answer_the_diff_and_the_collapsed_thinking()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp, "diff --git a/x b/x\n+added line\n-removed line");

        string html = SessionHtmlWriter.Write(session, "parser refactor", CredentialRedactor.Empty);

        Assert.Contains("the answer", html, StringComparison.Ordinal);
        Assert.Contains(
            "<span class=\"diff-add\">+added line</span>",
            html,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "<span class=\"diff-del\">-removed line</span>",
            html,
            StringComparison.Ordinal
        );
        Assert.Contains("weighing the options", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"thinking\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"thinking\" open", html, StringComparison.Ordinal);
        Assert.Contains(
            "<meta http-equiv=\"Content-Security-Policy\" content=\""
                + SessionHtmlWriter.ContentSecurityPolicy
                + "\">",
            html,
            StringComparison.Ordinal
        );
        Assert.Contains("<title>parser refactor</title>", html, StringComparison.Ordinal);
        // The document carries the session's own creation time, so the expectation is derived from
        // it. A literal date here passes only on the day the test was written.
        Assert.Contains(
            session
                .Header.Created.ToUniversalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            html,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_document_is_self_contained_and_has_no_script_and_no_external_reference()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp, "output");

        string html = SessionHtmlWriter.Write(session, "title", CredentialRedactor.Empty);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Hostile_tool_output_stays_inert_text()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(
            temp,
            "before <script>alert(1)</script> and <img src=x onerror=alert(1)> after"
        );

        string html = SessionHtmlWriter.Write(session, "title", CredentialRedactor.Empty);

        Assert.Contains(
            "before &lt;script&gt;alert(1)&lt;/script&gt; and &lt;img src=x onerror=alert(1)&gt; after",
            html,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        // The only attributes in the document are the writer's own static ones.
        Assert.DoesNotContain("onerror=\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redacted_thinking_renders_the_marker_and_not_the_signature()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("s_redacted.jsonl"), temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "think"));
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new TextReasoningContent(string.Empty) { ProtectedData = "signed-bytes" },
                    new TextContent("the answer"),
                ]
            )
        );

        string html = SessionHtmlWriter.Write(session, "title", CredentialRedactor.Empty);

        Assert.Contains("[redacted thinking]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-bytes", html, StringComparison.Ordinal);
        Assert.Contains("the answer", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_credential_never_reaches_the_document()
    {
        using var temp = new TempDirectory();
        Session session = BuildSession(temp, "token sk-openai-123456 here");

        string html = SessionHtmlWriter.Write(
            session,
            "title",
            new CredentialRedactor([("openai", "sk-openai-123456")])
        );

        Assert.DoesNotContain("sk-openai-123456", html, StringComparison.Ordinal);
        Assert.Contains("[redacted:openai]", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A turn with every interesting kind: a user message, an assistant thinking block and answer, a
    /// tool call, and its result carrying <paramref name="toolOutput"/>.
    /// </summary>
    private static Session BuildSession(TempDirectory temp, string toolOutput)
    {
        Session session = Session.Create(temp.File("s_20261024-093000-abcd.jsonl"), temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, "look at the parser"));
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [new TextReasoningContent("weighing the options"), new TextContent("the answer")]
            ),
            "claude-sonnet-4-5"
        );
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "bash", new Dictionary<string, object?>())]
            ),
            "claude-sonnet-4-5"
        );
        session.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", toolOutput)]),
            "claude-sonnet-4-5"
        );
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "done"), "claude-sonnet-4-5");
        return session;
    }
}
