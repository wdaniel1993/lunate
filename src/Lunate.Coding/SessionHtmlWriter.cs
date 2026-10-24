using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

/// <summary>
/// Renders one session as a single self-contained HTML document (design decisions 5 and 7). It
/// reads only <see cref="SessionEntry"/> — no TUI type, no width arithmetic — inlines its CSS, emits
/// no JavaScript and loads nothing from outside the file, so the document opens offline and is safe
/// to hand to someone else.
///
/// Everything taken from the session is escaped into a text node by one helper. Content never
/// becomes markup: a tool result carrying <c>&lt;script&gt;</c> renders as those characters, not as
/// an element, which is what the inert-content test pins.
/// </summary>
internal static class SessionHtmlWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The exact policy the document carries; no default-src means no fetch of any kind.</summary>
    internal const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; img-src data:";

    private const string Style = """
        :root { color-scheme: light dark; }
        body { margin: 0 auto; max-width: 60rem; padding: 2rem 1.5rem 4rem;
               font: 15px/1.55 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }
        header, footer { color: #666; }
        h1 { font-size: 1.4rem; margin: 0 0 .25rem; }
        .meta { color: #666; font-size: .85rem; margin: .15rem 0; }
        .entry { margin: 1.5rem 0; padding-left: .85rem; border-left: 3px solid #d0d0d0; }
        .entry.user { border-left-color: #4a7bd0; }
        .entry.assistant { border-left-color: #3f9e63; }
        .entry.tool { border-left-color: #b58a2a; }
        .entry-head { display: flex; gap: .75rem; align-items: baseline; margin-bottom: .35rem; }
        .role { font-weight: 600; text-transform: lowercase; }
        .entry-head time { color: #777; font-size: .8rem; }
        .text p, .section-text p { margin: .35rem 0; white-space: pre-wrap; }
        pre { margin: .35rem 0; padding: .6rem .75rem; overflow-x: auto; border-radius: 4px;
              background: rgba(127,127,127,.12); font: 13px/1.45 ui-monospace, SFMono-Regular,
              Menlo, Consolas, monospace; white-space: pre-wrap; word-break: break-word; }
        pre.args { color: #555; }
        .diff-add { color: #1f7a3f; }
        .diff-del { color: #a12626; }
        .tool-name { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
                     font-weight: 600; }
        details { margin: .35rem 0; }
        summary { cursor: pointer; color: #555; }
        .redacted { color: #777; font-style: italic; }
        hr.turn { border: 0; border-top: 1px dashed #d0d0d0; margin: 2rem 0 0; }
        ul.nested { margin: .35rem 0; padding-left: 1.25rem; font-size: .9rem; }
        """;

    /// <summary>The whole document, one string, ready to be written as UTF-8 without a BOM.</summary>
    internal static string Write(Session session, string title, CredentialRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(redactor);
        var document = new StringBuilder();
        document.AppendLine("<!DOCTYPE html>");
        document.AppendLine("<html lang=\"en\">");
        document.AppendLine("<head>");
        document.AppendLine("<meta charset=\"utf-8\">");
        document
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
            .Append(ContentSecurityPolicy)
            .AppendLine("\">");
        document.AppendLine(
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        );
        document.Append("<title>").Append(Text(title, redactor)).AppendLine("</title>");
        document.Append("<style>").AppendLine();
        document.AppendLine(Style);
        document.AppendLine("</style>");
        document.AppendLine("</head>");
        document.AppendLine("<body>");
        document.AppendLine("<header>");
        document.Append("<h1>").Append(Text(title, redactor)).AppendLine("</h1>");
        document
            .Append("<p class=\"meta\">session ")
            .Append(Text(session.SessionId, redactor))
            .Append(" · started ")
            .Append(Stamp(session.Header.Created))
            .Append(" · ")
            .Append(session.Entries.Count.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" entries · ")
            .Append(Text(session.Header.Cwd, redactor))
            .AppendLine("</p>");
        document.AppendLine("</header>");
        document.AppendLine("<main>");

        var turnOpened = false;
        foreach (SessionEntry entry in session.Entries)
        {
            if (entry is SessionMessageEntry { Message.Role.Value: "user" })
            {
                if (turnOpened)
                {
                    document.AppendLine("<hr class=\"turn\">");
                }

                turnOpened = true;
            }

            AppendEntry(document, entry, redactor);
        }

        document.AppendLine("</main>");
        document.AppendLine("<footer>");
        document.AppendLine(
            "<p class=\"meta\">Exported from Lunate. May contain file contents and command output; review before sharing.</p>"
        );
        document.AppendLine("</footer>");
        document.AppendLine("</body>");
        document.AppendLine("</html>");
        return document.ToString();
    }

    /// <summary>The document as the bytes the export writes: UTF-8, no BOM, the platform's newlines.</summary>
    internal static byte[] Bytes(Session session, string title, CredentialRedactor redactor) =>
        Utf8NoBom.GetBytes(Write(session, title, redactor));

    private static void AppendEntry(
        StringBuilder html,
        SessionEntry entry,
        CredentialRedactor redactor
    )
    {
        switch (entry)
        {
            case SessionMessageEntry message:
                AppendMessage(html, message, redactor);
                break;
            case SessionCompactionEntry compaction:
                html.AppendLine("<section class=\"entry\">");
                AppendHead(html, "compaction", compaction.Timestamp);
                AppendParagraphs(html, compaction.Summary, redactor);
                html.Append("<p class=\"meta\">replaces ")
                    .Append(Text(string.Join(", ", compaction.Replaces), redactor))
                    .AppendLine("</p>");
                html.AppendLine("</section>");
                break;
            case SessionBranchSummaryEntry summary:
                html.AppendLine("<section class=\"entry\">");
                AppendHead(html, "branch summary", summary.Timestamp);
                AppendParagraphs(html, summary.Summary, redactor);
                html.AppendLine("</section>");
                break;
            case SessionModelChangeEntry change:
                html.Append("<p class=\"meta\">").Append(Stamp(change.Timestamp));
                html.Append(" · model → ").Append(Text(change.Model, redactor)).AppendLine("</p>");
                break;
            case SessionActiveToolsEntry tools:
                html.Append("<p class=\"meta\">").Append(Stamp(tools.Timestamp));
                html.Append(" · tools: ")
                    .Append(Text(string.Join(", ", tools.Tools), redactor))
                    .AppendLine("</p>");
                break;
            case SessionPromptSectionEntry section:
                html.Append("<details class=\"prompt-section\"><summary>system prompt (")
                    .Append(Text(section.Section, redactor))
                    .Append(")</summary>");
                AppendPre(html, null, section.Text, redactor, diffClasses: false);
                html.AppendLine("</details>");
                break;
            case SessionChildSessionEntry child:
                html.Append("<p class=\"meta\">").Append(Stamp(child.Timestamp));
                html.Append(" · child session ")
                    .Append(Text(child.ChildSessionId, redactor))
                    .Append(" (run ")
                    .Append(Text(child.RunId, redactor))
                    .AppendLine(")</p>");
                break;
            case SessionNestedCallsEntry nested:
                html.Append("<div class=\"entry\">")
                    .Append("<p class=\"meta\">")
                    .Append(Stamp(nested.Timestamp))
                    .Append(" · nested calls for ")
                    .Append(Text(nested.CallId, redactor))
                    .AppendLine("</p><ul class=\"nested\">");
                foreach (SessionNestedCall call in nested.Calls)
                {
                    html.Append("<li><span class=\"tool-name\">")
                        .Append(Text(call.Name, redactor))
                        .Append("</span> ")
                        .Append(Text(call.Args, redactor))
                        .Append(" · ")
                        .Append(Text(call.Status, redactor))
                        .Append(" · ")
                        .Append(call.DurationMs.ToString("N0", CultureInfo.InvariantCulture))
                        .AppendLine(" ms</li>");
                }

                html.AppendLine("</ul></div>");
                break;
            case SessionExtensionEntry extension:
                html.Append("<details class=\"extension\"><summary>")
                    .Append(Text(extension.ExtensionType, redactor))
                    .Append(" ")
                    .Append(Stamp(extension.Timestamp))
                    .Append("</summary>");
                AppendPre(html, null, extension.PayloadJson, redactor, diffClasses: false);
                html.AppendLine("</details>");
                break;
            case SessionActiveLeafEntry leaf:
                html.Append("<p class=\"meta\">").Append(Stamp(leaf.Timestamp));
                html.Append(" · active leaf → ")
                    .Append(Text(leaf.LeafId, redactor))
                    .AppendLine("</p>");
                break;
            case SessionHeaderEntry:
                break;
            default:
                html.Append("<p class=\"meta\">[entry ")
                    .Append(Text(entry.Id, redactor))
                    .AppendLine("]</p>");
                break;
        }
    }

    private static void AppendMessage(
        StringBuilder html,
        SessionMessageEntry entry,
        CredentialRedactor redactor
    )
    {
        string role = entry.Message.Role.Value;
        string cssRole = role switch
        {
            "user" => "user",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "other",
        };
        html.Append("<section class=\"entry ").Append(cssRole).AppendLine("\">");
        string label =
            role == "assistant" && entry.Model is { Length: > 0 } model
                ? "assistant · " + model
                : role;
        AppendHead(html, label, entry.Timestamp);

        foreach (AIContent content in entry.Message.Contents)
        {
            switch (content)
            {
                case TextContent text when text.Text.Length > 0:
                    AppendParagraphs(html, text.Text, redactor);
                    break;
                case TextReasoningContent thinking:
                    AppendThinking(html, thinking, redactor);
                    break;
                case FunctionCallContent call:
                    html.Append("<p><span class=\"tool-name\">")
                        .Append(Text(call.Name ?? "tool", redactor))
                        .AppendLine("</span></p>");
                    AppendPre(
                        html,
                        "args",
                        Arguments(call.Arguments),
                        redactor,
                        diffClasses: false
                    );
                    break;
                case FunctionResultContent result:
                    AppendPre(html, "tool-result", Result(result), redactor, diffClasses: true);
                    break;
                case DataContent data:
                    html.Append("<p class=\"meta\">[image ")
                        .Append(Text(data.MediaType ?? "binary", redactor))
                        .AppendLine("]</p>");
                    break;
                case UriContent uri:
                    html.Append("<p class=\"meta\">[image ")
                        .Append(Text(uri.MediaType ?? "binary", redactor))
                        .AppendLine("]</p>");
                    break;
                case ErrorContent error:
                    AppendPre(html, "tool-result", error.Message, redactor, diffClasses: false);
                    break;
                default:
                    html.Append("<p class=\"meta\">[")
                        .Append(Text(content.GetType().Name, redactor))
                        .AppendLine("]</p>");
                    break;
            }
        }

        html.AppendLine("</section>");
    }

    /// <summary>
    /// Thinking is included and collapsed: a <c>&lt;details&gt;</c> element is the whole mechanism,
    /// which is why the document needs no script. Thinking the model returned only a signature for
    /// (or that the provider redacted) renders <c>[redacted thinking]</c>.
    /// </summary>
    private static void AppendThinking(
        StringBuilder html,
        TextReasoningContent thinking,
        CredentialRedactor redactor
    )
    {
        string text = thinking.Text;
        bool redacted = text.Length == 0 && !string.IsNullOrEmpty(thinking.ProtectedData);
        if (text.Length == 0 && !redacted)
        {
            return;
        }

        html.AppendLine("<details class=\"thinking\"><summary>thinking</summary>");
        if (redacted)
        {
            html.AppendLine("<pre class=\"redacted\">[redacted thinking]</pre>");
        }
        else
        {
            AppendPre(html, null, text, redactor, diffClasses: false);
        }

        html.AppendLine("</details>");
    }

    private static void AppendHead(StringBuilder html, string role, DateTimeOffset timestamp)
    {
        html.Append("<div class=\"entry-head\"><span class=\"role\">")
            .Append(Escape(role))
            .Append("</span><time>")
            .Append(Stamp(timestamp))
            .AppendLine("</time></div>");
    }

    /// <summary>Message text as paragraphs, so a long answer is readable without any markup pass.</summary>
    private static void AppendParagraphs(
        StringBuilder html,
        string text,
        CredentialRedactor redactor
    )
    {
        html.Append("<div class=\"text\">");
        foreach (
            string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n")
        )
        {
            if (paragraph.Length == 0)
            {
                continue;
            }

            html.Append("<p>").Append(Text(paragraph, redactor)).AppendLine("</p>");
        }

        html.AppendLine("</div>");
    }

    /// <summary>
    /// One preformatted block. <paramref name="diffClasses"/> colours unified-diff lines from a tool
    /// result: the <c>+</c> and <c>-</c> lines become spans, and nothing else about the text
    /// changes — every line is escaped either way.
    /// </summary>
    private static void AppendPre(
        StringBuilder html,
        string? cssClass,
        string text,
        CredentialRedactor redactor,
        bool diffClasses
    )
    {
        html.Append("<pre")
            .Append(cssClass is null ? string.Empty : " class=\"" + cssClass + "\"")
            .Append('>');
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string? marker =
                diffClasses
                && line.Length > 0
                && !line.StartsWith("+++", StringComparison.Ordinal)
                && !line.StartsWith("---", StringComparison.Ordinal)
                    ? line[0] switch
                    {
                        '+' => "diff-add",
                        '-' => "diff-del",
                        _ => null,
                    }
                    : null;
            if (marker is not null)
            {
                html.Append("<span class=\"").Append(marker).Append("\">");
                html.Append(Text(line, redactor));
                html.Append("</span>");
            }
            else
            {
                html.Append(Text(line, redactor));
            }

            if (index < lines.Length - 1)
            {
                html.Append('\n');
            }
        }

        html.AppendLine("</pre>");
    }

    /// <summary>The one escaping helper: session text becomes a text node, never markup.</summary>
    internal static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private static string Text(string text, CredentialRedactor redactor) =>
        Escape(redactor.Redact(text));

    private static string Stamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        + " UTC";

    private static string Arguments(IDictionary<string, object?>? arguments) =>
        arguments is null ? "{}" : JsonSerializer.Serialize(arguments);

    private static string Result(FunctionResultContent result) =>
        result.Result switch
        {
            null => string.Empty,
            string text => text,
            var value => JsonSerializer.Serialize(value),
        };
}
