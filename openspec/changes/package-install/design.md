## Context

The single-file work shipped the whole machine an install needs: an in-process restore through the NuGet client libraries, a lock whose graph is bound into a trust record, a compile that requires a trusted digest first, and a cache keyed by everything that went into the build. ADR-0017 names the other format in one sentence — "a packaged extension is a `.nupkg` with `extension.json`, an entry assembly and its dependencies; `lunate install <id>` installs it".

This change fills that sentence in, and widens it: what people want from a package is not only an extension but the skills, and later the prompt templates and themes, that go with it. The constraint from the product direction is sharper than the feature: reuse the restore, lock, trust and cache component; do not grow a second one. The design is shaped by taking that literally — an install is a *download* plus a file write, and everything downstream is a directory the host already discovers.

What exists today, and is not changed: `ExtensionLoader` scans `~/.lunate/extensions/` and `<worktree>/.lunate/extensions/`; a packaged extension is a directory with `extension.json`; project extensions are trusted by a content hash over the directory; skills are scanned from `<scope>/skills/` and `<worktree>/.{lunate,agents,claude}/skills/`; `lunate trust` lists every pending decision; `lunate cache prune` owns `cache/extensions/` only.

## Goals and non-goals

**Goals.** One command to put a published package on a machine; pinned by construction; a record that says exactly what was written and can prove it; uninstall that removes exactly that; two scopes that never silently shadow each other; no new trust mechanism; no second restore.

**Non-goals.** A marketplace, search, package discovery, signing, provenance, self-update of Lunate, git sources, and the template and theme kinds — the last two because the product has no consumer for them yet.

## Decisions

**D1 — The unit is a NuGet package id, at an exact version.** `lunate install <id>` resolves the latest stable once and records exactly what it resolved; `lunate install <id>@<version>` pins. A range, a wildcard or a floating version is a usage error: a range belongs to a dependency someone declared, not to a user's install, and "which version am I running" must have one answer. A path is refused because a directory is not a distribution unit — that is what copying into `.lunate/extensions/` is for. A git URL is refused by name; see Deferred.

**D2 — A package carries `lunate.package.json` at its root, and content in the folders it names.** The extension manifest stays exactly what it is.

```json
{ "schema": 1, "extension": "extension", "skills": "skills" }
```

```text
lunate.package.json
extension/            extension.json + the published files
skills/<name>/        one SKILL.md per skill, plus its files
```

At least one kind, and only the kinds this version installs. Why not reuse `extension.json` as the package manifest: that file requires exactly one of `entryAssembly` and `runtime`, so a skills-only bundle could not be expressed at all, and a package whose manifest doubles as the extension's would tie two lifecycles together for no gain. Unknown fields are ignored, so a package written for a later version still parses. The `extension` folder is copied verbatim; its `extension.json` is parsed by the existing parser, so `apiVersion`, `entryAssembly` and the declaration cross-checks behave exactly as they do for a hand-placed directory.

**D3 — Install is a download, not a restore.** One new public method on `NuGetRestoreEngine` — `DownloadAsync(id, exactVersion)` — resolves and downloads through the same `NuGetPackageFeed` the single-file restore uses: the same configuration chain rooted at a directory, the same credentials, the same feed-request observer that makes "zero requests" an observation rather than an inference. It stores the nupkg in the global packages folder, which is where NuGet already puts packages and where the user's SDK looks. It does not walk dependencies, restore a reference pack, compile, or write an extension lock, because there is nothing to resolve and nothing to build.

The bundle reader, the install record and the installer live in the same assembly, `Lunate.Extensibility.Restore`, because it is the one assembly that references `NuGet.*` and the existing suite asserts that no `NuGet.*` assembly is loaded before a restore. An install loads them; a session start still never does. The manifest itself is plain JSON — `schema`, `extension`, `skills` — parsed with `System.Text.Json` beside the archive reader.

**D4 — A package is self-contained; declared NuGet dependencies are refused.** A bundle is the output of a publish, and its dependencies are files inside it. Walking a graph and materialising a closure would be a second resolution path with its own collision rules (two packages shipping one assembly), its own asset selection and its own lock — exactly the second component this change is told not to grow. Refusing a package that declares dependencies, naming them, keeps "installed" and "runnable" the same word. The ADR's "accepted at install, ignored at load" rule still applies to a bundle that carries a copy of `Lunate.Extensibility.Abstractions`: install does not refuse it, and the load already warns once and uses the host's copy.

**D5 — Content lands in the scopes that already exist.** No new discovery path, no new registry, no new index.

| What | User scope | Project scope |
| --- | --- | --- |
| record | `<root>/packages/<id>/install.json` | `<worktree>/.lunate/packages/<id>/install.json` |
| extension | `<root>/extensions/<extension-id>/` | `<worktree>/.lunate/extensions/<extension-id>/` |
| skills | `<root>/skills/<name>/` | `<worktree>/.lunate/skills/<name>/` |

`<root>` is `LUNATE_HOME` when set, otherwise `~/.lunate`; a user install follows the root exactly as sessions, trust and user extensions do, and runs the same store guard (a root inside the workspace is refused before the first write) because it writes the store. `<id>` is the package id lowercased, because NuGet ids are case-insensitive and so is the filesystem on two of three platforms; the record keeps the canonical spelling. Nothing is written to `~/.agents/skills` — that is a convention shared with other agents, and Lunate's own user skill scope is already `<root>/skills`. Nothing is written under `cache/`: an installed packaged extension is not compiled, so it has no builds, and the extension cache stays what it is. `NUGET_PACKAGES` gains the nupkg and nothing else, and is never pruned by Lunate.

**D6 — Trust does not change, and install writes none of it.** An installed extension is a directory with a manifest, so the existing project extension question applies to it unchanged: id, version, directory, and a content hash over every file, so a file added after the prompt re-prompts. Installed skills are ordinary skills in a scanned scope, so the existing project skills question covers them in the same pass. User-scope content is trusted by installation, exactly as a global extension and a user skill already are. Install therefore touches `trust.json` not at all; it changes the pending set that `lunate trust` prints, which is what makes the non-interactive story work without a new flag.

Install is a CLI command and nothing else: no tool, slash command or protocol path wraps it, so a model cannot put a package into the user's store — the same line the trust model already draws around approvals.

Two consequences worth stating. A project install and a project extension dropped in by hand are indistinguishable to the trust model, which is correct: the question is about bytes at a path, not about how they arrived. And an install that produces byte-identical content keeps an existing decision, exactly as editing a file back to its approved content does — the decision was always about the bytes.

**D7 — One record, and no lock file.** The record has two halves, the way `extension.lock.json` does: `package` (sources, canonical id, exact version, nupkg SHA-512, the archive's file list) and `content` (extension id and version, skill names, and every written path with its size and SHA-256), plus scope, install time and an integrity hash over its own canonical bytes. A separate lock would exist to stop a compile from re-resolving; nothing here compiles, and a second file with the same version and hash in it is a second thing to keep consistent. A record that does not answer for itself reads `broken` and no command acts on it. This is deliberately *not* a committed file: it names this machine's scope paths and the feeds this machine used. A team that wants reproducibility pins `id@version`; making install read a `lunate.packages.json` of those pins is the deferred follow-up, and `lunate packages --json` already prints the pins.

**D8 — Pinning, update and the commit point.** No automatic update, no background check, no version check at session start: an update is a decision, and it happens when the user types it. `update <id>` moves to the latest stable on the configured feeds; `update <id>@<version>` moves to that version including a downgrade; `--all` does the scope. Every install and update downloads and verifies into `<scope>/packages/<id>/staging/`, replaces the recorded paths, and writes the record last, so the record is the commit point: a failure before it leaves the previous content and record byte-identical, and a failure after it cannot happen. Staging is deleted on failure and on the next run.

**D9 — Uninstall deletes what the record lists.** Not a directory sweep: the recorded paths, then the directories the install created that are now empty, then the record directory. A recorded file whose hash changed refuses the delete unless `--force`, because the user edited it and a command that silently discards an edit is a bug. A file the package did not write is left and named, with the directory it sits in, because a half-emptied extension directory looks like a broken extension and the user should know rather than discover it at the next session. Trust records are left alone: trust is about bytes at a path, and uninstall does not make a claim about the user's intent. A session that already loaded the extension keeps it until it exits; install and uninstall are CLI commands and never run inside a session.

**D10 — Scope is explicit and collisions are refusals.** User is the default; `--project` writes under the worktree, and outside one it is a usage error. The extension id is the collision key, across both scopes and against hand-placed and single-file extensions alike: an install that would create a duplicate extension id is refused, naming both providers and the uninstall that clears one. Discovery would produce the same error at the next session start; refusing at install is where the user can act on it. A skill directory that already exists is never written into — that is someone else's skill — and install says whose. A same-named skill in the *other* scope is the existing shadowing rule, so install warns once and the index keeps one winner. Nothing silently overrides; no scope wins because it was scanned later.

**D11 — Supply chain: what is in, and what is not.** In: an exact id match (no search, no suggestions, no "did you mean"), the full identity printed before anything is written (id, exact version, source, SHA-512, size, contents), `--dry-run`, path confinement for every manifest path and archive entry, entry-count and expanded-size bounds checked before extraction, per-file hashes in the record, and `--verify` to prove the content later. Out: author and repository signature verification, provenance attestations, vulnerability scanning, an SBOM. The SHA-512 is transport integrity against the feed, not a statement about who wrote the package, and the docs and the command's own output say so.

There is no install prompt. Install runs no code, so its identity is printed rather than asked about, and the only question on this path is the existing project trust question — which is not changed (open question 4). A prompt on install would ask the user to approve something that has not happened yet, and the thing worth approving ("this code runs") is exactly what the load-time question already asks.

## Failure, offline and private feeds

Feeds and credentials come from NuGet's configuration chain rooted at the working directory, so a repository's `nuget.config` applies to a project install and the user's to a user install. No interactive credential prompt exists, here or in the single-file restore; a 401 names the source and the configuration file to fix.

`--offline` resolves only from the global packages folder — the nupkg path NuGet's own resolver computes — and makes no feed request at all, not even a version lookup. The test observes the feed seam and asserts zero calls, the same measurement the single-file suite already uses for a stale digest. A cold folder fails with the package and the packages folder named, and writes nothing.

Every command is all-or-nothing in the sense that matters: no content exists that no record names. `--force` repairs a partial or hand-edited install by rewriting the recorded paths from the recorded version's nupkg, which is also the answer to "the copy is broken, I do not want to upgrade".

## Risks

- **A user-scope install is one command away from running code.** That is already true of anything dropped in `~/.lunate/extensions/`. Mitigations: the identity is printed in full, nothing updates itself, `--dry-run` exists, and project scope asks before use. Signature verification is the real fix and is deferred.
- **The copied files duplicate bytes already in the packages folder.** Accepted: the extension directory is the format the loader documents, a copy is what makes uninstall exact, and a published extension's payload is small next to the nupkg cache.
- **A bundle can ship an assembly the host owns.** Handled by the existing load rule (host copy wins, one warning) rather than by a new install refusal, so a package that makes the mistake still works.
- **Refusing cross-scope duplicates will annoy someone** with a user-level favourite and a project-level pin. The message names the install to remove; real precedence is a deferred decision, not an accident of scan order.
- **A package can be large enough to hurt.** Bounds are checked before extraction; there is no streaming or resumable download in v1.
- **A crash between the content write and the record write leaves files no record names.** The window is small and safe — untrusted, unrecorded content cannot load in a project — but it is real. The next install of that package replaces it, and `--force` repairs the recorded case; cleaning up unrecorded directories is deliberately not automated, because deleting directories no record names is how an installer eats someone's work.

## Deferred

Marketplace and search; author signing and provenance; git URLs (a clone would need a commit pin, its own auth story and either a build toolchain or prebuilt content — the last is the only version worth building, and it is the same layout with a different fetch, so it is its own change); `templates` and `themes` as installable kinds (the registry reserves the slot and no template source ships); a committed `lunate.packages.json` that install reads; in-session install (`/install`); automatic updates; `--source` to pick a feed; Windows and Linux RID validation beyond what a self-contained bundle already carries; cache eviction; self-update of Lunate; distributing subagents as packages.

## Open questions

1. **git URLs in v1?** Recommend no: refuse by name and keep the design to one fetch, one auth story and one identity. The shape is known — clone at a commit, require the same `lunate.package.json` with prebuilt content, bind the commit SHA into the record — and it is a change of its own.
2. **`lunate.package.json`, or `extension.json` as the bundle manifest?** Recommend the separate file: a skills-only bundle has no runtime to declare, and the extension manifest should stay a description of an extension.
3. **The command surface.** Recommend `install`, `update`, `uninstall` and `packages` (a noun command for the list, like `extensions` and `cache`), and no bare `lunate list`, which reads as sessions and leaves no room for other lists.
4. **Should the project trust question name the package that installed the directory?** Recommend no in v1. The question is about bytes at a path and already names id, version and directory; provenance is in `lunate packages` and in the record. Adding it would change a shipped requirement for a line the user can get in one command.
5. **Are installed skills plain scope skills or extension-contributed?** Recommend plain scope landing: it works for a bundle with no extension, it needs no new API, and it inherits the scope's existing trust question instead of a second one.
6. **Refuse bundles that declare NuGet dependencies?** Recommend yes, for the reason in D4. The alternative — silently ignoring them — makes "installed" a lie, and materialising them rebuilds the resolution path this change exists to avoid.
7. **One record or a record plus a lock?** Recommend one, for the reason in D7.
8. **Default scope user, `--project` opt-in?** Recommend yes, matching extensions and skills; a project install writes repository-visible state, so it should be typed.
9. **When does signature verification land?** Recommend before anything marketplace-shaped ships, and not in this change.
