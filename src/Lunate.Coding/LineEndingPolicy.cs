using System.Text;
using System.Text.RegularExpressions;

namespace Lunate.Coding;

/// <summary>
/// The convention a new file gets, in the order the rule names it (AGENTS.md, Rules): the nearest
/// <c>.editorconfig</c> that sets the property, then <c>.gitattributes</c>, else the platform
/// default. Only the properties the rule needs are read — <c>end_of_line</c> and <c>charset</c> — and
/// only the pattern syntax a project actually uses: <c>*</c>, <c>**</c>, <c>?</c>, <c>{a,b}</c>, and
/// basename matching for a pattern without a slash, the way editorconfig and git both read one.
/// </summary>
/// <remarks>
/// A file that already exists never comes here: its own endings and byte-order mark are the answer,
/// whatever a policy above it says. This class is read-only and touches nothing on disk.
/// </remarks>
internal static class LineEndingPolicy
{
    internal const string Lf = "\n";

    internal const string CrLf = "\r\n";

    private const string EditorConfigFileName = ".editorconfig";

    private const string GitAttributesFileName = ".gitattributes";

    /// <summary>The ending a new file at <paramref name="path"/> gets.</summary>
    internal static string ResolveEnding(string path) =>
        EditorConfig(path).EndOfLine ?? GitAttributes(path) ?? Environment.NewLine;

    /// <summary>
    /// Whether a new file at <paramref name="path"/> starts with a byte-order mark. Only
    /// <c>.editorconfig</c> carries this property; git attributes have no equivalent.
    /// </summary>
    internal static bool ResolveBom(string path) =>
        string.Equals(EditorConfig(path).Charset, "utf-8-bom", StringComparison.Ordinal);

    private readonly record struct EditorSettings(string? EndOfLine, string? Charset);

    /// <summary>
    /// The settings that apply, resolved the way editorconfig resolves them: from the outermost file
    /// inward, so a closer file and a later matching section both win, and <c>root = true</c> ends
    /// the search upward at the file that declares it.
    /// </summary>
    private static EditorSettings EditorConfig(string path)
    {
        string full = Path.GetFullPath(path);
        List<(string Directory, string Text)> chain = [];
        string? directory = Path.GetDirectoryName(full);
        while (directory is not null)
        {
            string file = Path.Combine(directory, EditorConfigFileName);
            if (File.Exists(file))
            {
                string text = ReadShared(file);
                chain.Add((directory, text));
                if (IsRoot(text))
                {
                    break;
                }
            }

            string? parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent;
        }

        string? endOfLine = null;
        string? charset = null;
        chain.Reverse();
        foreach ((string baseDirectory, string text) in chain)
        {
            Apply(
                text,
                Path.GetRelativePath(baseDirectory, full).Replace('\\', '/'),
                ref endOfLine,
                ref charset
            );
        }

        return new EditorSettings(endOfLine, charset);
    }

    private static void Apply(
        string text,
        string relativePath,
        ref string? endOfLine,
        ref string? charset
    )
    {
        string? section = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1];
                continue;
            }

            int separator = line.IndexOf('=');
            if (section is null || separator <= 0 || !Matches(section, relativePath))
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().ToLowerInvariant();
            if (string.Equals(key, "end_of_line", StringComparison.OrdinalIgnoreCase))
            {
                endOfLine = value switch
                {
                    "lf" => Lf,
                    "crlf" => CrLf,
                    _ => endOfLine,
                };
            }
            else if (
                string.Equals(key, "charset", StringComparison.OrdinalIgnoreCase)
                && value is "utf-8" or "utf-8-bom"
            )
            {
                charset = value;
            }
        }
    }

    private static bool IsRoot(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("root", StringComparison.OrdinalIgnoreCase))
            .Any(line =>
                line["root".Length..].Trim() is ['=', .. var value]
                && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            );

    /// <summary>
    /// The nearest <c>eol</c> attribute that matches, searched from the repository root down so a
    /// nested attributes file, and a later line in one file, both win. The walk stops at the
    /// repository root: an attributes file above it belongs to another repository.
    /// </summary>
    private static string? GitAttributes(string path)
    {
        string full = Path.GetFullPath(path);
        List<(string Directory, string Text)> chain = [];
        string? directory = Path.GetDirectoryName(full);
        while (directory is not null)
        {
            string file = Path.Combine(directory, GitAttributesFileName);
            if (File.Exists(file))
            {
                chain.Add((directory, ReadShared(file)));
            }

            string git = Path.Combine(directory, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent;
        }

        string? endOfLine = null;
        chain.Reverse();
        foreach ((string baseDirectory, string text) in chain)
        {
            string relativePath = Path.GetRelativePath(baseDirectory, full).Replace('\\', '/');
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !Matches(fields[0].TrimStart('/'), relativePath))
                {
                    continue;
                }

                foreach (string attribute in fields[1..])
                {
                    if (attribute == "eol=lf")
                    {
                        endOfLine = Lf;
                    }
                    else if (attribute == "eol=crlf")
                    {
                        endOfLine = CrLf;
                    }
                }
            }
        }

        return endOfLine;
    }

    /// <summary>
    /// Whether one pattern matches the path, with editorconfig's and git's shared rule: a pattern
    /// without a slash is matched against the file name, one with a slash against the whole
    /// relative path.
    /// </summary>
    private static bool Matches(string pattern, string relativePath)
    {
        bool nameOnly = !pattern.Contains('/');
        string subject = nameOnly ? Path.GetFileName(relativePath) : relativePath;
        return Regex.IsMatch(
            subject,
            "^" + Translate(pattern) + "$",
            RegexOptions.CultureInvariant
        );
    }

    private static string Translate(string pattern)
    {
        var translated = new StringBuilder();
        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '*')
            {
                bool recursive = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (recursive && index + 2 < pattern.Length && pattern[index + 2] == '/')
                {
                    translated.Append("(?:.*/)?");
                    index += 2;
                }
                else if (recursive)
                {
                    translated.Append(".*");
                    index++;
                }
                else
                {
                    translated.Append("[^/]*");
                }
            }
            else if (character == '?')
            {
                translated.Append("[^/]");
            }
            else if (character == '{' && pattern.IndexOf('}', index) is int close and > 0)
            {
                string[] options = pattern[(index + 1)..close].Split(',');
                translated.Append("(?:").AppendJoin('|', options.Select(Regex.Escape)).Append(')');
                index = close;
            }
            else
            {
                translated.Append(Regex.Escape(character.ToString()));
            }
        }

        return translated.ToString();
    }

    /// <summary>
    /// Reads a policy file without denying anyone else's handle: an editor saving the same file
    /// must not fail the write, and a write must not fail the read.
    /// </summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }
}
