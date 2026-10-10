using System.Text;
using System.Text.RegularExpressions;

namespace Lunate.Coding;

/// <summary>
/// The pure <c>.gitignore</c> engine: one file's patterns parsed into translated regexes. Blank
/// lines and <c>#</c> comments are skipped; <c>!</c> negates; a trailing <c>/</c> is
/// directory-only; trailing spaces are trimmed (an escaped trailing space is unsupported); a
/// pattern with a non-trailing <c>/</c> is anchored to the file's directory and otherwise matches
/// the basename at any depth; within one file the last matching pattern wins. Cross-file
/// precedence (deeper wins) and directory pruning live in <see cref="FileIndex"/>.
/// </summary>
internal sealed class GitIgnore
{
    private readonly List<Rule> _rules;

    private GitIgnore(List<Rule> rules) => _rules = rules;

    /// <summary>Parses one .gitignore file; blank and comment lines are skipped.</summary>
    public static GitIgnore Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        List<Rule> rules = [];
        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.TrimEnd(' ', '\r');
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            bool negated = trimmed[0] == '!';
            if (negated)
            {
                trimmed = trimmed[1..];
            }

            bool directoryOnly = trimmed.EndsWith('/');
            if (directoryOnly)
            {
                trimmed = trimmed[..^1];
            }

            if (trimmed.Length == 0)
            {
                continue;
            }

            bool anchored = trimmed[0] == '/';
            if (anchored)
            {
                trimmed = trimmed[1..];
            }
            else
            {
                anchored = trimmed.Contains('/');
            }

            rules.Add(new Rule(negated, directoryOnly, Translate(trimmed, anchored)));
        }

        return new GitIgnore(rules);
    }

    /// <summary>
    /// One file's verdict for a path relative to its directory: whether the path is ignored
    /// (<c>true</c>), re-included (<c>false</c>), or untouched by any pattern (<c>null</c>; an
    /// outer file's verdict then stands). The last matching pattern wins.
    /// </summary>
    public bool? Match(string relativePath, bool isDirectory)
    {
        bool? ignored = null;
        foreach (Rule rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (rule.Matcher.IsMatch(relativePath))
            {
                ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    private static Regex Translate(string pattern, bool anchored)
    {
        var builder = new StringBuilder("^");
        if (!anchored)
        {
            builder.Append("(?:.*/)?");
        }

        var index = 0;
        while (index < pattern.Length)
        {
            char current = pattern[index];
            if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                {
                    builder.Append("(?:.*/)?");
                    index += 3;
                    continue;
                }

                if (index + 2 == pattern.Length && (index == 0 || pattern[index - 1] == '/'))
                {
                    builder.Append(".*");
                    index += 2;
                    continue;
                }
            }

            switch (current)
            {
                case '*':
                    builder.Append("[^/]*");
                    index++;
                    break;
                case '?':
                    builder.Append("[^/]");
                    index++;
                    break;
                case '[':
                    index = TranslateClass(pattern, index, builder);
                    break;
                case '\\' when index + 1 < pattern.Length:
                    builder.Append(Regex.Escape(pattern[index + 1].ToString()));
                    index += 2;
                    break;
                default:
                    builder.Append(Regex.Escape(current.ToString()));
                    index++;
                    break;
            }
        }

        builder.Append('$');
        return new Regex(
            builder.ToString(),
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
        );
    }

    /// <summary>
    /// Translates one <c>[...]</c> class; returns the index after the class (or after the lone
    /// <c>[</c> when there is no closing bracket). A leading <c>!</c> negates; a literal <c>]</c>
    /// may open the class; regex metacharacters inside are escaped while ranges survive.
    /// </summary>
    private static int TranslateClass(string pattern, int start, StringBuilder builder)
    {
        var index = start + 1;
        if (index < pattern.Length && pattern[index] is '!' or '^')
        {
            index++;
        }

        if (index < pattern.Length && pattern[index] == ']')
        {
            index++;
        }

        while (index < pattern.Length && pattern[index] != ']')
        {
            index++;
        }

        if (index >= pattern.Length)
        {
            builder.Append("\\[");
            return start + 1;
        }

        string body = pattern[(start + 1)..index];
        bool negated = body.StartsWith('!');
        if (negated)
        {
            body = body[1..];
        }

        builder.Append(negated ? "[^" : "[");
        foreach (char character in body)
        {
            if (character is '\\' or '[' or ']' or '^')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append(']');
        return index + 1;
    }

    private sealed record Rule(bool Negated, bool DirectoryOnly, Regex Matcher);
}
