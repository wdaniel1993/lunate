namespace Lunate.Extensibility;

/// <summary>
/// The assemblies the host owns: an extension's own directory, or a package it locked, may carry a
/// copy of one, and the copy is never loaded — the host's is, so the contract types keep one
/// identity across the compile/load boundary. The rule is by simple name because that is what a
/// load binds by; the substitution is reported once per extension, naming who shipped the copy,
/// which is the one thing the user cannot see for themselves.
/// </summary>
public static class HostOwnedAssemblies
{
    /// <summary>
    /// The shared contract assemblies, in the order diagnostics name them: the extension contract,
    /// the two host assemblies its public signatures use, and <c>System.Text.Json</c>, whose types
    /// cross the same boundary. A copy of any of these is ignored wherever it is found.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Lunate.Extensibility.Abstractions",
        "Lunate.Agent",
        "Microsoft.Extensions.AI.Abstractions",
        "System.Text.Json",
    ];

    /// <summary>True when <paramref name="name"/> is one of <see cref="Names"/> (case-insensitive).</summary>
    public static bool IsHostOwned(string? name) =>
        name is not null && Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The host-owned assemblies sitting in <paramref name="directory"/> as <c>&lt;name&gt;.dll</c>,
    /// in <see cref="Names"/> order: what a packaged extension ships that the host will not load from
    /// there.
    /// </summary>
    public static IReadOnlyList<string> InDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return [.. Names.Where(name => File.Exists(Path.Combine(directory, name + ".dll")))];
    }

    /// <summary>
    /// The one line a load reports when it dropped a copy: what was ignored, who shipped it, and
    /// whose copy is live. <paramref name="owners"/> is rendered as written — a package list, or the
    /// extension's directory — so the same sentence serves both runtime kinds.
    /// </summary>
    public static string Describe(string extensionId, IReadOnlyList<string> names, string owners) =>
        FormattableString.Invariant(
            $"extension '{extensionId}': ignored the copy of {string.Join(", ", names)} shipped by {owners}; the host owns that assembly, so the host's copy is the one loaded."
        );
}
