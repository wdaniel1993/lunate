using System.Text;

namespace Lunate.Extensibility;

/// <summary>
/// The single-file extension header scanner: our own reader for the leading block of directives,
/// so the host never depends on the SDK's file-based-app parser. Only the leading block is
/// scanned — blank lines, comments and <c>#:</c> directives before the first C# token. A
/// <c>#:</c> line after code begins is an error naming its line, never a silently ignored
/// directive. <c>#:sdk</c>, <c>#:property</c> and <c>#:project</c> are refused; an unrecognised
/// directive inside the leading block warns and is ignored.
/// </summary>
/// <remarks>
/// The leading block ends at the first line that is not blank, a comment or a <c>#:</c> directive.
/// A <c>#</c> line that is not <c>#:</c> (for example <c>#nullable enable</c>) ends it too: the
/// header must be the first thing in the file, so no preprocessor state can move a declaration out
/// of the bytes the user approved.
/// </remarks>
public static partial class ExtensionHeaderScanner
{
    /// <summary>Scans <paramref name="source"/> and returns everything the header declares.</summary>
    public static ExtensionHeader Scan(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var builder = new Builder();
        var codeStarted = false;
        var inBlockComment = false;
        var line = 0;
        foreach (string raw in Lines(source))
        {
            line++;
            string content = StripComments(raw, ref inBlockComment);
            if (content.Length == 0)
            {
                continue;
            }

            if (content.StartsWith("#:", StringComparison.Ordinal))
            {
                if (codeStarted)
                {
                    builder.Errors.Add(
                        new ExtensionHeaderIssue(
                            line,
                            $"'{DirectiveToken(content)}' appears after the first C# token; directives "
                                + "are read only in the leading block before any code. Move it to the "
                                + "top of the file."
                        )
                    );
                    continue;
                }

                builder.Apply(content[2..], line);
                continue;
            }

            codeStarted = true;
        }

        return builder.Build();
    }

    /// <summary>The source's lines without their <c>\r</c>, so a CRLF file scans like an LF one.</summary>
    private static IEnumerable<string> Lines(string source)
    {
        var start = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\n')
            {
                continue;
            }

            yield return TrimCarriageReturn(source[start..index]);
            start = index + 1;
        }

        yield return TrimCarriageReturn(source[start..]);
    }

    private static string TrimCarriageReturn(string line) =>
        line.EndsWith('\r') ? line[..^1] : line;

    /// <summary>
    /// Removes the line's comments and trims it, carrying an open block comment across lines. A
    /// <c>#:</c> line inside a comment is therefore never read as a directive, before or after the
    /// code begins.
    /// </summary>
    private static string StripComments(string line, ref bool inBlockComment)
    {
        var text = new StringBuilder(line.Length);
        var index = 0;
        while (index < line.Length)
        {
            if (inBlockComment)
            {
                int close = line.IndexOf("*/", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    return text.ToString();
                }

                inBlockComment = false;
                index = close + 2;
                continue;
            }

            if (line[index] == '/' && index + 1 < line.Length)
            {
                if (line[index + 1] == '/')
                {
                    break;
                }

                if (line[index + 1] == '*')
                {
                    inBlockComment = true;
                    index += 2;
                    continue;
                }
            }

            text.Append(line[index]);
            index++;
        }

        return text.ToString().Trim();
    }

    /// <summary>The directive token as the author wrote it, for a message that names it.</summary>
    private static string DirectiveToken(string content)
    {
        int end = content.IndexOfAny([' ', '\t'], 2);
        return end < 0 ? content : content[..end];
    }
}
