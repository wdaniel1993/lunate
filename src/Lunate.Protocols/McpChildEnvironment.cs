namespace Lunate.Protocols;

/// <summary>
/// Builds the environment a stdio MCP child process starts with when known credential values are
/// withheld. Without it the child inherits the whole parent environment, including provider keys
/// and authenticated secrets Lunate holds in memory (#449/#455).
/// </summary>
internal static class McpChildEnvironment
{
    /// <summary>
    /// The child starts from Lunate's current environment minus every variable whose value exactly
    /// equals one of the withheld credential values. Nothing is allow-listed — what Lunate does
    /// not know stays inherited, so existing server setups keep working; only the exact values
    /// <c>CredentialRedactorSource.Collect</c> knows are removed. The server's declared
    /// <c>env</c> map is set on top and wins by design: it is the opt-in, and <c>${VAR}</c>
    /// expansion has already resolved its values at config load.
    /// <para>
    /// Names are compared Ordinal (OrdinalIgnoreCase on Windows, where PATH and Path would
    /// otherwise produce duplicate rows); values are compared Ordinal. Nothing here is
    /// culture-shaped, so a machine culture cannot change which variables arrive.
    /// </para>
    /// </summary>
    internal static Dictionary<string, string?> BuildChildEnvironment(
        IReadOnlyList<string> withheldValues,
        IReadOnlyDictionary<string, string>? declaredEnvironment
    )
    {
        var child = new Dictionary<string, string?>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Value is not string value || withheldValues.Contains(value))
            {
                continue;
            }

            child[(string)entry.Key] = value;
        }

        if (declaredEnvironment is not null)
        {
            foreach ((string name, string value) in declaredEnvironment)
            {
                child[name] = value;
            }
        }

        return child;
    }
}
