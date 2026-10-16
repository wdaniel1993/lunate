namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A source of portable skills contributed by an extension: its id plus the skill directories it
/// carries. v1 is disk-only — every directory must be a skill directory (containing a
/// <c>SKILL.md</c>) on disk inside the extension's own folder; the core scans and validates them
/// exactly like any other skill. Registration is registration only: it must not start processes,
/// sockets or timers.
/// </summary>
public interface ISkillSource
{
    /// <summary>The source's id; diagnostics name it next to the owning extension.</summary>
    string Id { get; }

    /// <summary>The skill directories, absolute or relative to the extension's own folder.</summary>
    IReadOnlyList<string> SkillDirectories { get; }
}
