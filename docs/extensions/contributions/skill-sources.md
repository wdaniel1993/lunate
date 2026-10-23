# Skill sources

An extension can contribute portable skills — directories containing a `SKILL.md` — by
registering an `ISkillSource`. The host scans and validates them exactly like project and user
skills; they join the session's skill index tagged with the contributing extension.

## The API

```csharp
public interface ISkillSource
{
    string Id { get; }

    IReadOnlyList<string> SkillDirectories { get; }
}
```

```csharp
context.RegisterSkillSource(new DirectorySkillSource("main", Path.Combine(root, "skills")));
```

`SkillDirectories` lists the directories; the sample resolves them from the extension
assembly's directory.

## Constraints

- **Disk only.** This version has no in-memory or generated skill source: every directory must
  exist on disk and contain a `SKILL.md`. The core parses and validates it like any other skill.
- **Inside the extension.** A directory outside the extension's own folder is refused with a
  warning and skipped; so is a directory that does not exist.
- **Source id.** The id names the source in diagnostics. A duplicate id is replaced by the last
  registration, with a warning naming both extensions.
- **Scan cap.** At most 256 skill directories are scanned per source; the rest are skipped with a
  warning.
- **No manifest field.** Skill sources are registration-only; there is no `skills` entry in
  `extension.json`.

Contributed skills inherit the extension's trust and disable state: a project extension that was
not trusted never loads, so its skills never appear; `--no-extensions` removes them all. They are
listed by `/skills` with the scope label `extension '<id>'`.

Their index lines are charged against the contributing extension's context budget — the same
`HookRunnerOptions.ContextBudgetPerExtension` value used by hooks, default 2000 estimated tokens —
before the global 1000-token index cap applies. A line that does not fit is dropped and logged; it
stays visible in `/skills` as not indexed.

## Example

There is no sample under `samples/extensions/` that contributes skills today. The loader fixture
`SkillSourceExtension` is the reference:

```csharp
public sealed class SkillSourceExtension : IExtension { }

public sealed class SkillSourceExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        string root = Path.GetDirectoryName(typeof(SkillSourceExtensionFactory).Assembly.Location)!;
        context.RegisterSkillSource(new DirectorySkillSource("main", Path.Combine(root, "skills")));
        return new SkillSourceExtension();
    }
}

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
```

Full file:
[`SkillSourceExtensionFactory.cs`](../../../tests/TestExtensions/SkillSourceExtension/SkillSourceExtensionFactory.cs).

## See also

- Skills discovery, trust and index budget: [`docs/spec/extensibility.md`](../../spec/extensibility.md#contract-and-loading)
- The skills a session loads: [`README.md`](../../../README.md#skills)
- Settings that change budgets: [settings-and-secrets.md](settings-and-secrets.md)
