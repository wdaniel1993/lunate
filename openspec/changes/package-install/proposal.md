## Why

A single file is now an extension, but a package is not: there is no way to take a published NuGet package that carries an extension, a set of skills, or both, and put it on a machine. Everything the format needs already exists — the NuGet client libraries, the lock discipline, the content-hash trust model, the scope directories the host already scans — and the ADR names packaged installation as the other half of the extension story. Without it, "extend it from a single file to a full NuGet package" stops at the file.

The risk this change is written against is the obvious wrong answer: a second installer with its own restore, its own record and its own trust decision. The design below adds a *download* on top of the existing restore assembly, writes only directories the host already discovers, and records no trust at all.

## What Changes

- **`lunate install <package-id>[@<version>]`** puts a package's content on the machine: the extension it carries lands in the extensions scope, its skills in the skills scope, and one record ties them together. `lunate update` moves a package to a newer pinned version, `lunate uninstall` removes exactly what was written, and `lunate packages` lists what is installed and verifies it on request.
- **A package is a NuGet package with a `lunate.package.json` at its root** naming the content folders it carries (`extension`, `skills`). The extension manifest stays exactly what it is; a bundle that ships no code at all is a valid package.
- **Install is a download, not a restore.** One new method on the existing restore engine resolves an exact version and downloads it through the same feed chain, credentials and feed-request seam the single-file restore uses. There is no dependency walk, no reference pack and no compile, because a package is prebuilt and self-contained — and a package that declares NuGet dependencies is refused rather than half-installed.
- **No new trust mechanism, and no trust record written by install.** A user-scope install is trusted the way a global extension already is; a project-scope install stays untrusted until the existing extension question and the existing project-skills question are answered. Install changes the *pending set* `lunate trust` lists, and nothing else.
- **Pinned by construction.** An install records the exact version, the nupkg's SHA-512 and every file it wrote with its hash. There is no automatic update and no range at the command line; `update` is explicit and either completes or leaves the previous install byte-identical.
- **Scope is explicit.** The default is the user scope under `LUNATE_HOME`; `--project` writes under the worktree's `.lunate/`. Nothing is written to `~/.agents/skills`, to the extension build cache, or to `NUGET_PACKAGES` beyond the nupkg the download put there.

## Capabilities

### New Capabilities

- `packages`: the installable unit and its manifest, where content lands per scope, the install record and its integrity rules, pinning and update, uninstall, offline and private-feed behaviour, and the four commands.

### Modified Capabilities

None. Install produces exactly the inputs the existing requirements describe — a packaged extension directory, skill directories in a scanned scope, and pending trust decisions — so discovery, loading, trust and skills require no change. That is the point of the design, not an omission.

## Impact

- `src/Lunate.Extensibility.Restore`: the download method on `NuGetRestoreEngine`, the bundle reader, the install record and the installer — this is the one assembly that references `NuGet.*`, and the lazy-load rule keeps it that way. No new package, no dependency walk.
- `src/Lunate.Coding`: `install`, `update`, `uninstall` and `packages` subcommands, one view file, and the `Cli` dispatch entries.
- `src/Lunate.Extensibility` and `Lunate.Extensibility.Abstractions` are unchanged: discovery, loading, trust and the lock types do not move.
- `docs/extensions/`: a packages page, plus links from `single-file.md` and `manifest.md`; `docs/README.md` gains the row. `adr/0017-addendum-packages.md` records the durable decisions.
- No new NuGet packages. `NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Configuration`, `NuGet.Frameworks` and `NuGet.Versioning` are already approved by ADR-0017 and cover the download and the archive reader.
