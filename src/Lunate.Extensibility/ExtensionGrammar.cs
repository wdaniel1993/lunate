namespace Lunate.Extensibility;

/// <summary>
/// The header's value grammar: the manifest's id rule, a NuGet version's shape and a NuGet range's
/// shape. This is a syntax check, not a semantic one — <c>NuGet.Versioning</c> arrives with the
/// restore in the next phase, and nothing here decides whether a package exists.
/// </summary>
internal static class ExtensionGrammar
{
    /// <summary>
    /// The extension id's shape, which is the packaged manifest's rule exactly: the header may not
    /// accept an id a package would refuse, because a single file is meant to be promotable to a
    /// package without renaming itself. The rule lives in <c>ExtensionIds</c> and is shared with
    /// <c>extension.json</c>.
    /// </summary>
    internal static bool IsValidExtensionId(string value) =>
        Abstractions.ExtensionIds.IsValid(value);

    /// <summary>
    /// The manifest's provider-id rule (<c>[a-z0-9-]+</c>), which <c>#:provider</c> mirrors: the
    /// id becomes <c>modelProviders[].id</c>, so it has to satisfy the same rule.
    /// </summary>
    internal static bool IsValidProviderId(string value) =>
        value.Length > 0
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>A NuGet package id's shape: letters, digits, <c>.</c>, <c>_</c> and <c>-</c>.</summary>
    internal static bool IsValidPackageId(string value) =>
        value.Length > 0
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
        );

    /// <summary>
    /// One exact version: two to four numeric parts, an optional <c>-prerelease</c> and an
    /// optional <c>+metadata</c>. A range marker, a floating <c>*</c> or whitespace fails, which is
    /// what makes <c>#:extension</c>'s version exact.
    /// </summary>
    internal static bool IsExactVersion(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var index = 0;
        var parts = 0;
        while (true)
        {
            int start = index;
            while (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                index++;
            }

            if (index == start)
            {
                return false;
            }

            parts++;
            if (index == value.Length)
            {
                break;
            }

            if (value[index] != '.')
            {
                break;
            }

            if (parts == 4)
            {
                return false;
            }

            index++;
            if (index == value.Length)
            {
                return false;
            }
        }

        if (parts < 2)
        {
            return false;
        }

        if (index < value.Length && value[index] == '-')
        {
            index++;
            if (!ConsumeIdentifiers(value, ref index))
            {
                return false;
            }
        }

        if (index < value.Length && value[index] == '+')
        {
            index++;
            if (!ConsumeIdentifiers(value, ref index))
            {
                return false;
            }
        }

        return index == value.Length;
    }

    /// <summary>
    /// A version, a NuGet range or a floating version: <c>1.0.0</c>, <c>[1.0.0]</c>,
    /// <c>[1.0.0,2.0.0)</c>, <c>(1.0.0,)</c>, <c>14.*</c>, <c>1.2.*</c>. Spaces inside the brackets
    /// are allowed. A floating version resolves to the highest published match at lock time, which
    /// the lock then pins, so the approved graph still names exact versions.
    /// </summary>
    internal static bool IsVersionRange(string value)
    {
        string text = value.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (IsFloatingVersion(text))
        {
            return true;
        }

        if (text[0] is not ('[' or '('))
        {
            return IsExactVersion(text);
        }

        if (text[^1] is not (']' or ')'))
        {
            return false;
        }

        string inner = text[1..^1];
        int comma = inner.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return text[0] == '[' && text[^1] == ']' && IsExactVersion(inner.Trim());
        }

        if (inner.IndexOf(',', comma + 1) >= 0)
        {
            return false;
        }

        string lower = inner[..comma].Trim();
        string upper = inner[(comma + 1)..].Trim();
        if (lower.Length == 0 && upper.Length == 0)
        {
            return false;
        }

        return (lower.Length == 0 || IsExactVersion(lower))
            && (upper.Length == 0 || IsExactVersion(upper));
    }

    /// <summary>
    /// A floating version: <c>*</c> (the latest stable), a numeric prefix with a trailing wildcard
    /// (<c>14.*</c>, <c>1.2.*</c>, <c>1.2.3.*</c>), or a numbered version with a prerelease wildcard
    /// (<c>1.2.3-*</c>). Anything else — <c>1.*.0</c>, <c>14*x</c>, a wildcard with no numeric part —
    /// is refused, so a typo cannot quietly become "latest".
    /// </summary>
    private static bool IsFloatingVersion(string text)
    {
        if (text == "*")
        {
            return true;
        }

        if (text.EndsWith("-*", StringComparison.Ordinal))
        {
            return IsExactVersion(text[..^2]);
        }

        if (!text.EndsWith(".*", StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = text[..^2].Split('.');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part.Length == 0 || !part.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One or more dot-separated <c>[0-9A-Za-z-]</c> identifiers (prerelease or metadata).</summary>
    private static bool ConsumeIdentifiers(string value, ref int index)
    {
        var count = 0;
        while (index < value.Length)
        {
            int start = index;
            while (
                index < value.Length
                && (char.IsAsciiLetterOrDigit(value[index]) || value[index] == '-')
            )
            {
                index++;
            }

            if (index == start)
            {
                return false;
            }

            count++;
            if (index == value.Length)
            {
                break;
            }

            if (value[index] == '+')
            {
                break;
            }

            if (value[index] != '.')
            {
                return false;
            }

            index++;
            if (index == value.Length)
            {
                return false;
            }
        }

        return count > 0;
    }
}
