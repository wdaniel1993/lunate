using Lunate.Extensibility.Abstractions;

namespace SkillSourceExtension;

/// <summary>The fixture extension carrying portable skills: it registers one disk-only
/// <see cref="ISkillSource"/> whose directories are the subdirectories of its own
/// <c>skills/</c> folder, so the harness can prove tagged, budgeted, trust-inheriting
/// contributions.</summary>
public sealed class SkillSourceExtension : IExtension { }

public sealed class SkillSourceExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string root = Path.GetDirectoryName(typeof(SkillSourceExtensionFactory).Assembly.Location)!;
        context.RegisterSkillSource(new DirectorySkillSource("main", Path.Combine(root, "skills")));
        return new SkillSourceExtension();
    }
}

/// <summary>Each subdirectory of <c>root</c> is a skill directory.</summary>
internal sealed class DirectorySkillSource(string id, string root) : ISkillSource
{
    public string Id => id;

    public IReadOnlyList<string> SkillDirectories =>
        Directory.Exists(root)
            ?
            [
                .. Directory
                    .EnumerateDirectories(root)
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal),
            ]
            : [];
}
