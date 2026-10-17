using System.Text;

namespace Lunate.Coding;

/// <summary>The parsed frontmatter of a <c>SKILL.md</c>: the required name and description, the
/// command-only flag and the body after the closing fence. A null error means the frontmatter is
/// well-formed; the name is still checked against the directory name by the scanner.</summary>
internal sealed record SkillFrontmatter(
    string? Name,
    string? Description,
    bool CommandOnly,
    string Body,
    string? Error
);

/// <summary>
/// The deliberately small frontmatter reader: a leading <c>---</c> fence, <c>key: value</c> lines and
/// a closing <c>---</c>. No YAML engine (no packages), no nesting; unknown keys are ignored,
/// <c>allowed-tools</c> is accepted and discarded (never a permission grant) and
/// <c>disable-model-invocation: true</c> marks the skill command-only.
/// </summary>
internal static class SkillFrontmatterParser
{
    internal static SkillFrontmatter Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return Invalid("frontmatter must start with a '---' line");
        }

        string? name = null;
        string? description = null;
        var commandOnly = false;
        var closing = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim() == "---")
            {
                closing = index;
                break;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = Unquote(line[(separator + 1)..].Trim());
            switch (key)
            {
                case "name":
                    name = value;
                    break;
                case "description":
                    description = value;
                    break;
                case "disable-model-invocation":
                    commandOnly = IsTrue(value);
                    break;
                case "allowed-tools":
                    // Accepted and deliberately inert: the approval system is the only grant path.
                    break;
                default:
                    break;
            }
        }

        if (closing < 0)
        {
            return Invalid("frontmatter is not closed with a '---' line");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Invalid("frontmatter is missing a 'name'");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Invalid("frontmatter is missing a 'description'");
        }

        var body = new StringBuilder();
        for (var index = closing + 1; index < lines.Length; index++)
        {
            if (body.Length == 0 && string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            if (body.Length > 0)
            {
                body.Append('\n');
            }

            body.Append(lines[index]);
        }

        return new SkillFrontmatter(
            name,
            description,
            commandOnly,
            body.ToString().TrimEnd(),
            null
        );
    }

    private static SkillFrontmatter Invalid(string reason) =>
        new(null, null, false, string.Empty, reason);

    private static string Unquote(string value)
    {
        if (
            value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
        )
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value == "1";
}
