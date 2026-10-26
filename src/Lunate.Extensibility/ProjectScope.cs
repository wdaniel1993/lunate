namespace Lunate.Extensibility;

/// <summary>
/// The two directories a project-scope install writes into, relative to a worktree root (design D6):
/// <c>.lunate/extensions/&lt;extension-id&gt;/</c> and <c>.agents/skills/&lt;name&gt;/</c>. One place
/// states the layout, so the install target, the loader's provenance lookup and the trust digest
/// cannot disagree about where a project install lands.
/// </summary>
internal static class ProjectScope
{
    /// <summary>The directory that holds a project extension's folders, relative to the worktree.</summary>
    internal const string ExtensionsDirectory = ".lunate/extensions";

    /// <summary>The directory that holds a project skill's folders, relative to the worktree.</summary>
    internal const string SkillsDirectory = ".agents/skills";

    /// <summary>
    /// The other skill folders project discovery reads, in its precedence order. Only the install's
    /// target is written by Lunate; the rest are named so a shadowed skill can say who wins.
    /// </summary>
    internal static readonly string[] SkillDirectories =
    [
        ".lunate/skills",
        ".agents/skills",
        ".claude/skills",
    ];

    /// <summary>
    /// <paramref name="relative"/> under <paramref name="root"/>, with the forward slashes the
    /// layout is written with turned into this platform's separators, then <paramref name="more"/>.
    /// </summary>
    internal static string Under(string root, string relative, params string[] more) =>
        Path.Combine([root, relative.Replace('/', Path.DirectorySeparatorChar), .. more]);

    /// <summary>The folder name a relative directory starts with: <c>.lunate</c>, <c>.agents</c>…</summary>
    internal static string Kind(string relative) => relative[..relative.IndexOf('/')];
}
