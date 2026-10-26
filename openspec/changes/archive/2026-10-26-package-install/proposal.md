## Why

A single file is now an extension, but a package is not: there is no way to take a published NuGet package that carries an extension, a set of skills, or both, and put it on a machine. Everything an install needs already exists — the NuGet client libraries, the restore walker and its lock, the content-hash trust model, the scope directories the host already scans — and the ADR names packaged installation as the other half of the extension story. Without it, "extend it from a single file to a full NuGet package" stops at the file.

The risk this change is written against is the obvious wrong answer: a second installer with its own restore, its own record and its own trust decision. The design below adds a *download* and a *graph walk* on top of the existing restore assembly, writes only directories the host already discovers, and records no trust at all.

## What Changes

- **`lunate install <package-id>[@<version>]`** puts a package's content on the machine: the extension it carries lands in the extensions scope, its skills in the skills scope, and one record ties them together. `lunate uninstall` removes exactly what was written and `lunate packages` lists what is installed and verifies it on request. Installing a different version of an installed package **is** the upgrade — there is no separate `update` command in this version.
- **A package is a NuGet package with a `lunate.package.json` at its root** naming the content folders it carries (`extension`, `skills`). The extension manifest stays exactly what it is; a bundle that ships no code at all is a valid package.
- **A packaged extension may declare NuGet dependencies, and they are resolved through the phase-2a pipeline unchanged**: the same walker (and its conformance corpus), the same global packages folder, the same lock format with its graph/toolset split, host-owned assemblies excluded from the graph. The resolved assemblies are materialised beside the entry assembly, the shape the packaged loader already binds, so no load-time requirement changes. The lock lands in the existing cache location, `<root>/cache/extensions/<extension-id>/extension.lock.json`.
- **Trust is the single-file rule applied to a package.** A project-scope decision binds the package id@version, the nupkg's SHA-512 and the canonical lock graph — plus the existing directory content hash, because an installed extension's files are writable and an edit must still re-prompt. The question gains one line of provenance: `from package acme-tools@1.2.0 (nuget.org)`. Install itself writes no trust record; it only changes the pending set `lunate trust` lists.
- **Pinning is by construction.** An install records the exact version, the nupkg's SHA-512, the lock it resolved and every file it wrote with its hash. A failed install or upgrade leaves the previous install byte-identical: the record is the commit point.
- **Scope is explicit, and a project install is committable.** The default is the user scope under `LUNATE_HOME`; `--project` writes the extension into `<worktree>/.lunate/extensions/` and the skills into `<worktree>/.agents/skills/` so they travel with the repository, while the install record stays in the user's state root — the record is machine state, the content is the team's. `--project` never writes to the user's skill scope, and no scope silently shadows another.

## Capabilities

### New Capabilities

- `packages`: the installable unit and its manifest, dependency resolution through the existing pipeline, where content lands per scope, the install record and its integrity rules, pinning and upgrade, uninstall, offline and private-feed behaviour, and the three commands.

### Modified Capabilities

- `extensions` — *Project extension trust*: the question for an extension that arrived through an install names the source package, and its content hash covers the package identity (id@version, nupkg SHA-512, canonical lock graph) on top of the directory hash. Nothing else about the decision changes: same repository identity, same re-prompt on change, same `lunate trust` inventory.
- `skills` — *Project skills trust* and *The `/skills` view*: an installed skill's question shows the same provenance line, and `/skills` shows `from <package>@<version>` after the scope, so a user can tell a package's skill from a hand-written one.

These two are modified rather than extended because the prompt text and the digest inputs are existing requirements; describing them again in `packages` would leave two accounts of one question.

## Impact

- `src/Lunate.Extensibility.Restore`: the download, the package reader and installer, and the reuse of the existing restore walker for a package's declared dependencies — this is the one assembly that references `NuGet.*`, and the lazy-load rule keeps it that way. No new package.
- `src/Lunate.Extensibility`: the lock types and the loader are unchanged; install feeds the walker the package's dependency set instead of a source file's directives.
- `src/Lunate.Coding`: `install`, `uninstall` and `packages` subcommands, the provenance line in the trust question, the `/skills` provenance suffix, and the CLI dispatch entries.
- `docs/extensions/packages.md` plus links from `single-file.md`, `manifest.md` and `docs/README.md`; the project-install story (content committed, record local) is stated where the scopes are described.
- `adr/0017-addendum-packages.md` records the durable decisions: the manifest, dependency resolution through the existing pipeline, the trust digest, the record/lock split and the deferred list.
- No new NuGet packages. `NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Configuration`, `NuGet.Frameworks` and `NuGet.Versioning` are already approved by ADR-0017.
