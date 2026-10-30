using System.Text;

namespace Lunate.Coding;

/// <summary>Projects a concise command answer and preserves the captured streams as a transcript.</summary>
internal static class BashOutput
{
    private const int AnswerLineLimit = 20;
    private const int AnswerCharacterLimit = 2_000;

    internal static string Answer(string stdout, string stderr, string footer, string? hint) =>
        Compose(LastLines(stdout), stderr, footer, hint);

    internal static string Transcript(string stdout, string stderr) => Compose(stdout, stderr);

    private static string Compose(string stdout, string stderr)
    {
        var text = new StringBuilder(stdout);
        if (stderr.Length > 0)
        {
            if (text.Length > 0 && text[^1] != '\n')
            {
                text.Append('\n');
            }

            text.Append("stderr:\n").Append(stderr);
        }

        return text.ToString();
    }

    private static string Compose(string stdout, string stderr, string footer, string? hint)
    {
        var text = new StringBuilder(Compose(stdout, stderr));
        if (text.Length > 0 && text[^1] != '\n')
        {
            text.Append('\n');
        }

        text.Append(footer);
        if (hint is not null)
        {
            text.Append('\n').Append(hint).Append('\n');
        }

        return text.ToString();
    }

    private static string LastLines(string stdout)
    {
        if (stdout.Length == 0)
        {
            return string.Empty;
        }

        string[] lines = stdout.Split('\n');
        int end = stdout.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        int start = Math.Max(0, end - AnswerLineLimit);
        string selected = string.Join('\n', lines[start..end]);
        if (stdout.EndsWith('\n'))
        {
            selected += '\n';
        }

        if (selected.Length <= AnswerCharacterLimit)
        {
            return selected;
        }

        int cut = selected.Length - AnswerCharacterLimit;
        if (char.IsLowSurrogate(selected[cut]))
        {
            cut++;
        }

        return selected[cut..];
    }
}
