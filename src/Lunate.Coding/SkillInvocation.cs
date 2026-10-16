using System.Text;

namespace Lunate.Coding;

/// <summary>
/// The wrapped user turn <c>/skill:&lt;name&gt;</c> submits: the skill body inside
/// <c>&lt;skill name="…" dir="…"&gt;</c> so relative references resolve from the skill directory.
/// <c>$ARGUMENTS</c> in the body is substituted when present; otherwise non-empty arguments append
/// as a final <c>ARGUMENTS: …</c> line.
/// </summary>
internal static class SkillInvocation
{
    internal const string ArgumentsToken = "$ARGUMENTS";

    internal static string Wrap(SkillDescriptor skill, string arguments)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(arguments);

        bool substitutes = skill.Body.Contains(ArgumentsToken, StringComparison.Ordinal);
        string body =
            substitutes && arguments.Length > 0
                ? skill.Body.Replace(ArgumentsToken, arguments, StringComparison.Ordinal)
                : skill.Body;

        var text = new StringBuilder();
        text.Append("<skill name=\"")
            .Append(Escape(skill.Name))
            .Append("\" dir=\"")
            .Append(Escape(skill.Directory))
            .Append("\">")
            .Append('\n')
            .Append(body);
        if (!substitutes && arguments.Length > 0)
        {
            text.Append('\n').Append("ARGUMENTS: ").Append(arguments);
        }

        text.Append('\n').Append("</skill>");
        return text.ToString();
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
