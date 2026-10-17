namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// Builds a child process environment that can never inherit Lunate's own variables by omission:
/// the minimal base set (PATH, HOME/USERPROFILE, SYSTEMROOT, TEMP/TMP/TMPDIR, LANG), plus the
/// declared pass-through names, plus the declared values — nothing else. The one implementation
/// shared by the code-mode sample and the remote extension host.
/// </summary>
public static class EnvironmentScrubber
{
    /// <summary>The minimal base names every scrubbed child receives when the parent has them.</summary>
    public static IReadOnlyList<string> BaseNames { get; } =
    ["PATH", "HOME", "USERPROFILE", "SystemRoot", "TEMP", "TMP", "TMPDIR", "LANG"];

    /// <summary>
    /// Builds the exact environment: each base name present in the parent (read through
    /// <paramref name="read"/>, case-insensitively on Windows), each pass-through name, then the
    /// explicit values (which win).
    /// </summary>
    /// <param name="read">Reads the parent's environment; null reads the process environment.</param>
    /// <param name="set">Values to set; null sets none.</param>
    /// <param name="passthrough">Names inherited from the parent; null passes none through.</param>
    public static IReadOnlyDictionary<string, string> Build(
        Func<string, string?>? read = null,
        IReadOnlyDictionary<string, string>? set = null,
        IReadOnlyList<string>? passthrough = null
    )
    {
        read ??= Environment.GetEnvironmentVariable;
        // Windows environment names are case-insensitive: a declared name differing only in case
        // from a base or pass-through name must replace it, never duplicate it.
        var environment = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
        foreach (string name in BaseNames)
        {
            if (read(name) is { Length: > 0 } value)
            {
                environment[name] = value;
            }
        }

        foreach (string name in passthrough ?? [])
        {
            if (read(name) is { } value)
            {
                environment[name] = value;
            }
        }

        foreach ((string name, string value) in set ?? new Dictionary<string, string>())
        {
            environment[name] = value;
        }

        return environment;
    }
}
