using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The one scrubbing path every host diagnostic takes before it reaches a diagnostic bundle or the
/// console: a spill-failure reason, an extension load reason, an OS error message. Turn content
/// never takes this path — the bundle measures it, it does not copy it. A diagnostic message is
/// reduced in a fixed order: exact-value credential redaction (<see cref="CredentialRedactor"/>
/// replaces a value Lunate holds with <c>[redacted:&lt;name&gt;]</c>), then URL userinfo and query
/// stripped so a link is scheme, authority and path only, then the home directory replaced by
/// <c>~</c>, then the user name removed from paths. Every diagnostic site goes through
/// <see cref="Scrub"/>; none scrubs on its own. Exceptions have a second shape: one whose message
/// echoes its input (<c>FormatException</c>, <c>JsonException</c>, <c>XmlException</c>,
/// <c>DecoderFallbackException</c>) is reduced to its type and the position it carries, and an
/// <see cref="IOException"/> carries its HResult category — code over prose.
/// </summary>
internal sealed class DiagnosticScrubber
{
    /// <summary>The identity scrub: redaction no-ops, URLs, the home and the name are untouched.</summary>
    internal static DiagnosticScrubber Empty { get; } = new(CredentialRedactor.Empty, null, null);

    internal static DiagnosticScrubber FromEnvironment(CredentialRedactor redactor) =>
        new(
            redactor,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName
        );

    internal DiagnosticScrubber(CredentialRedactor redactor, string? home, string? userName)
    {
        _redactor = redactor;
        _home = home;
        _userName = userName;
    }

    private readonly CredentialRedactor _redactor;
    private readonly string? _home;
    private readonly string? _userName;

    /// <summary>The one message path, in order: redact, strip URLs, replace home, remove the user name.</summary>
    internal string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return RemoveUserNameSegments(ReplaceHome(StripUrls(_redactor.Redact(text))));
    }

    /// <summary>
    /// The diagnostic form of an exception. Message text (and source paths) of the general and
    /// echo-prone forms pass through <see cref="Scrub"/>; the credential redaction inside it is
    /// the exact-value rule, and nothing else is inferred.
    /// </summary>
    internal string Describe(Exception exception) =>
        exception switch
        {
            FormatException => "FormatException",
            JsonException json => JsonForm(json),
            XmlException xml => XmlForm(xml),
            DecoderFallbackException decoder => DecoderForm(decoder),
            // Windows answers a write into a directory with UnauthorizedAccessException, not IOException.
            // The reader cares about the category, not the .NET type, so both take the same shape —
            // otherwise the same failure reads differently per platform and a test that waits for
            // "IOException (" times out on Windows only.
            IOException io => IOExceptionForm(io),
            UnauthorizedAccessException denied => IOExceptionForm(denied),
            _ => GeneralForm(exception),
        };

    private string JsonForm(JsonException exception)
    {
        string form = "JsonException";
        if (exception.LineNumber is { } line)
        {
            form += " line=" + line.ToString(CultureInfo.InvariantCulture);
        }

        if (exception.BytePositionInLine is { } position)
        {
            form += " byte=" + position.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(exception.Path))
        {
            form += " path=" + Scrub(exception.Path);
        }

        return form;
    }

    private string XmlForm(XmlException exception)
    {
        string form =
            "XmlException line="
            + exception.LineNumber.ToString(CultureInfo.InvariantCulture)
            + " position="
            + exception.LinePosition.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(exception.SourceUri))
        {
            form += " in=" + Scrub(exception.SourceUri);
        }

        return form;
    }

    private static string DecoderForm(DecoderFallbackException exception) =>
        exception.Index >= 0
            ? "DecoderFallbackException index="
                + exception.Index.ToString(CultureInfo.InvariantCulture)
            : "DecoderFallbackException";

    private string IOExceptionForm(Exception exception)
    {
        string form = "IOException (" + Category(exception.HResult) + ")";
        string message = Scrub(exception.Message);
        return message.Length == 0 ? form : form + ": " + message;
    }

    private string GeneralForm(Exception exception)
    {
        string form = exception.GetType().Name;
        string message = Scrub(exception.Message);
        return message.Length == 0 ? form : form + ": " + message;
    }

    /// <summary>
    /// The HResult category: a short stable token per the table, anything else as
    /// <c>0x&lt;hex&gt;</c>. So a reader can tell a full disk from a permission problem even when
    /// the message itself is scrubbed to nothing.
    /// </summary>
    internal static string Category(int hresult) =>
        hresult switch
        {
            unchecked((int)0x80070070) => "disk_full",
            unchecked((int)0x80070005) => "access_denied",
            unchecked((int)0x80070020) => "sharing_violation",
            unchecked((int)0x80070002) => "not_found",
            unchecked((int)0x80070003) => "not_found",
            unchecked((int)0x800700CE) => "path_too_long",
            unchecked((int)0x8007007B) => "invalid_name",
            unchecked((int)0x80070027) => "handle_disk_full",
            _ => string.Create(CultureInfo.InvariantCulture, $"0x{hresult:x8}"),
        };

    /// <summary>
    /// The exact home replaced by <c>~</c> where it is a clean path prefix: the character after the
    /// match is <c>/</c>, <c>\</c> or the end of the text, so <c>/Users/dbuser2</c> is untouched.
    /// </summary>
    private string ReplaceHome(string text)
    {
        if (string.IsNullOrEmpty(_home) || _home.Length > text.Length)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int position = 0;
        int index = text.IndexOf(_home, StringComparison.Ordinal);
        while (index >= 0)
        {
            int end = index + _home.Length;
            if (end == text.Length || text[end] is '/' or '\\')
            {
                builder.Append(text, position, index - position);
                builder.Append('~');
                position = end;
                index = text.IndexOf(_home, end, StringComparison.Ordinal);
            }
            else
            {
                index = text.IndexOf(_home, index + 1, StringComparison.Ordinal);
            }
        }

        if (position == 0)
        {
            return text;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    /// <summary>
    /// The user name removed from paths when it is long enough to be distinct: the
    /// <c>/Users/&lt;name&gt;/…</c> form becomes <c>~/…</c>, and a bare <c>&lt;name&gt;</c> left
    /// inside a path segment is removed.
    /// </summary>
    private string RemoveUserNameSegments(string text)
    {
        if (string.IsNullOrEmpty(_userName) || _userName.Length <= 2)
        {
            return text;
        }

        string name = Regex.Escape(_userName);
        text = Regex.Replace(
            text,
            @"(?<=[/\\])Users[/\\]?" + name + @"(?=[/\\])",
            "~",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)
        );
        return Regex.Replace(
            text,
            @"[/\\]" + name + @"(?=[/\\])",
            string.Empty,
            RegexOptions.None,
            TimeSpan.FromSeconds(1)
        );
    }

    /// <summary>
    /// Every URL token rebuilt as scheme, authority and path only. A token that does not parse is
    /// left unchanged: credential redaction already ran, so nothing secret survives.
    /// </summary>
    private static string StripUrls(string text)
    {
        var builder = new StringBuilder(text.Length);
        int position = 0;
        int index = text.IndexOf("://", StringComparison.Ordinal);
        while (index >= 0)
        {
            int schemeStart = SchemeStart(text, index);
            int end = TokenEnd(text, index + 3);
            string token = text[schemeStart..end];
            if (
                Uri.TryCreate(token, UriKind.Absolute, out Uri? uri)
                && uri.Scheme + "://" + uri.Authority + uri.AbsolutePath is { } rebuilt
                && !string.Equals(rebuilt, token, StringComparison.Ordinal)
            )
            {
                builder.Append(text, position, schemeStart - position);
                builder.Append(rebuilt);
                position = end;
            }

            index = text.IndexOf("://", index + 3, StringComparison.Ordinal);
        }

        if (position == 0)
        {
            return text;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    /// <summary>The start of the scheme a <c>://</c> belongs to: the scheme characters before it.</summary>
    private static int SchemeStart(string text, int separator)
    {
        int start = separator;
        while (
            start > 0
            && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '+' or '-')
        )
        {
            start--;
        }

        return start;
    }

    /// <summary>The first whitespace at or after <paramref name="start"/>, else the end.</summary>
    private static int TokenEnd(string text, int start)
    {
        for (int index = start; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return text.Length;
    }
}
