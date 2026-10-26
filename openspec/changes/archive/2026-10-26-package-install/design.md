## Context

The single-file work shipped the whole machine an install needs: an in-process restore through the NuGet client libraries, a walker with a conformance corpus, a lock whose graph is bound into a trust record, a compile that requires a trusted digest first, and a cache keyed by everything that went into the build. ADR-0017 names the other format in one sentence — "a packaged extension is a `.nupkg` with `extension.json`, an entry assembly and its dependencies; `lunate install <id>` installs it".

This change fills that sentence in, and widens it: what people want from a package is not only an extension but the skills that go with it. The constraint from the product direction is sharper than the feature: reuse the restore, lock, trust and cache component; do not grow a second one.

Packages exist for the extensions that are more than one file. Those depend on libraries, so a packaged format that refuses dependencies would be barely more capable than a single file — which is why the dependency walk is part of this design rather than deferred with the marketplace.

What exists today, and is not changed: `ExtensionLoader` scans `<root>/extensions/` and `<worktree>/.lunate/extensions/`; a packaged extension is a directory with `extension.json` and its assemblies beside it; project extensions are trusted by a content hash over the directory; skills are scanned from `<scope>/skills/` and `<worktree>/.{lunate,agents,claude}/skills/`; `lunate trust` lists every pending decision; `lunate cache prune` owns `cache/extensions/`.

## Goals and non-goals

**Goals.** One command to put a published package on a machine; dependencies resolved by the existing pipeline; pinned by construction; a record that says exactly what was written and can prove it; uninstall that removes exactly that; a project install whose content can be committed; no new trust mechanism; no second restore.

**Non-goals.** A marketplace, search, package discovery, signing, provenance, self-update of Lunate, git sources, a separate `update` command, and the template and theme kinds — the last two because the product has no consumer for them yet.

## Decisions

**D1 — The unit is a NuGet package id, at an exact version; a second install of a different version is the upgrade.** `lunate install <id>` resolves the latest stable once and records exactly what it resolved; `lunate install <id>@<version>` pins. A range, a wildcard or a floating version is a usage error: a range belongs to a dependency someone declared, not to a user's install, and "which version am I running" must have one answer. A path is refused because a directory is not a distribution unit — that is what copying into `.lunate/extensions/` is for. A git URL is refused by name.

There is no `update` command. `lunate install <id>@<newer>` on an installed package replaces it in place — the old files are removed through the record, the new content is staged and written, the record goes last — and the spec describes that as the upgrade path, so a future `update` (latest-stable convenience, `--all`) can be added without changing the meaning of anything here.

**D2 — A package carries `lunate.package.json` at its root, and content in the folders it names.** The extension manifest stays exactly what it is.

```json
{ "schema": 1, "extension": "extension", "skills": "skills" }
```

```text
lunate.package.json
extension/            extension.json + the published files
skills/<name>/        one SKILL.md per skill, plus its files
```

At least one kind, and only the kinds this version installs. Why not reuse `extension.json` as the package manifest: that file requires exactly one of `entryAssembly` and `runtime`, so a skills-only bundle could not be expressed at all, and a package whose manifest doubles as the extension's would tie two lifecycles together for no gain. Unknown fields are ignored, so a package written for a later version still parses. The `extension` folder is copied verbatim apart from the dependency materialisation in D4; its `extension.json` is parsed by the existing parser, so `apiVersion`, `entryAssembly` and the declaration cross-checks behave exactly as they do for a hand-placed directory.

**D3 — Install is a download plus a graph walk, and the walk is the phase-2a walk.** Two calls through `NuGetPackageFeed`, with the same configuration chain, credentials and feed-request observer that makes "zero requests" an observation rather than an inference: resolve and download the exact package version, then walk the dependency set the package declares. The walk is the existing one — the same resolver, the same conformance corpus, the same global packages folder, the same lock writer with its graph/toolset halves and its host-owned exclusion — because that machinery already answers the questions a package's dependencies raise (asset selection, framework and RID choice, collision, host-owned copies) and a second answer would drift.

What is *not* reused is the compile. A package is published output; there is nothing to compile, so install writes no build and touches no build cache.

**D4 — Dependencies are resolved and materialised; a dependency that cannot be resolved is a refusal.** The walk produces a closure; the installer copies each resolved assembly (and its satellite assets) into the extension directory beside the entry assembly, which is the layout the packaged loader already binds. The loader therefore does not change — this is the whole reason the format stays compatible with ADR-0017. Assemblies the host owns are excluded from the graph by the same rule that excludes them for a single file, and a bundle that ships its own copy of the contract still installs and still loads the host's.

A dependency the feeds do not serve, a version conflict the walker cannot resolve, or a dependency whose assets fit no target is refused, naming the dependency and the feeds tried, before anything is written. Ignoring dependencies would make "installed" a lie; a half-materialised closure would make it a trap.

**D5 — The lock is the existing lock, in the existing place.** The walk writes `extension.lock.json` — same format, same graph and toolset halves, same canonical bytes for hashing — into `<root>/cache/extensions/<extension-id>/`, where every single-file lock already lives. It is not a second file type and not a second location, so `lunate cache prune` already owns the directory; prune leaves an installed package's entry alone, because unlike a build it cannot be regenerated from a source file, and a reinstall is the repair.

**D6 — Content lands in the scopes that already exist; a project install writes content into the repository and keeps its record in the state root.**

| What | User scope | Project scope |
| --- | --- | --- |
| record | `<root>/packages/<package-id>/install.json` | `<root>/packages/<package-id>/install.json` |
| extension | `<root>/extensions/<extension-id>/` | `<worktree>/.lunate/extensions/<extension-id>/` |
| skills | `<root>/skills/<name>/` | `<worktree>/.agents/skills/<name>/` |

`<root>` is `LUNATE_HOME` when set, otherwise `~/.lunate`. The project record stays in the state root because it is machine state — this machine's feeds, this machine's paths, this machine's install time — while the content is the team's: `.lunate/extensions/` and `.agents/skills/` are committed, teammates get the same bytes through git, and each of them trusts them through the existing question the first time. A project record therefore names the worktree it belongs to, and `lunate packages` in another worktree does not list it as installed.

The user-scope paths follow the root exactly as sessions, trust and user extensions do, and the existing store guard runs before the first write. `<package-id>` is lowercased, because NuGet ids are case-insensitive and so is the filesystem on two of three platforms; the record keeps the canonical spelling. A project install writes nothing to `<root>/skills`, nothing to `<root>/cache/extensions/<id>/builds/`, and `NUGET_PACKAGES` gains the nupkgs the download and the walk put there and is never pruned by Lunate.

**D7 — Trust does not change shape, and install writes none of it.** An installed extension is a directory with a manifest, so the existing project extension question applies to it: the same repository identity, the same live-area prompt, the same `lunate trust` inventory. What a package adds is **what the digest answers for** and **what the question says**:

- The decision binds the package identity the user actually chose — package id@version, the nupkg's SHA-512, and the canonical lock graph — the same rule a single-file extension already uses for its source and its packages.
- It also keeps the existing directory content hash. The package identity alone would not notice a file edited inside the installed directory, and an installed extension's files are writable in a way a compiled output is not; the union is the only version where "changed content re-prompts" stays true.
- The question gains one line: `from package acme-tools@1.2.0 (nuget.org)`. It is the most useful fact at the moment of decision, it is already in the record, and it costs a line.

Installed skills are ordinary skills in a scanned scope, so the existing project skills question covers them in the same pass and shows the same provenance line. User-scope content is *not* trusted by installation: a descriptor an install record owns is pending in either scope (phase 6, per `adr/0017-addendum-packages.md`), while a hand-placed user-scope extension stays trusted by placement — the delimiter is the record, not the folder. Install touches `trust.json` not at all; it changes the pending set `lunate trust` prints, which is what makes the non-interactive story work without a new flag.

Install is a CLI command and nothing else: no tool, slash command or protocol path wraps it, so a model cannot put a package into the user's store — the same line the trust model already draws around approvals.

**D8 — One record, and the lock is the dependency lock.** `install.json` has two halves, the way `extension.lock.json` does: `package` (sources, canonical id, exact version, nupkg SHA-512, the archive's file list, and the extension id whose cache entry holds the lock) and `content` (the extension id and version, the skill names, and every written path with its size and SHA-256), plus scope, the worktree it belongs to when project, install time and an integrity hash over its own canonical bytes. The lock is not duplicated into the record: it is the existing file in the existing place, named by the record rather than copied into it. A record that does not answer for itself reads `broken`: `packages` reports it and `uninstall` refuses it, and a reinstall is the repair — it writes the content and a new record and names the bytes it could not account for, rather than refusing to install over its own record or claiming a removal it cannot name (phase 6). `uninstall --force` is the other answer: the record's own directory and `<root>/extensions/<package-id>/`, with the paths printed and the unknowable ones named.

**D9 — Pinning, upgrade and the commit point.** No automatic update, no background check, no version check at session start. `install <id>@<version>` on an installed package downloads and verifies into `<root>/packages/<id>/staging/`, removes the old content through the old record, writes the new content, then writes the lock and the record — record last — so a failure before the record write leaves the previous install byte-identical and a failure after it cannot happen. Staging is deleted on failure and on the next run. A changed version means changed bytes, which means a changed digest, which means the project question is asked again; an identical reinstall keeps the existing decision, exactly as editing a file back to its approved content does.

**D10 — Scope is explicit and collisions are refusals.** User is the default; `--project` outside a worktree is a usage error. The extension id is the collision key, across both scopes and against hand-placed and single-file extensions alike: an install that would create a duplicate extension id is refused, naming both providers and the uninstall that clears one. Discovery would produce the same error at the next session start; refusing at install is where the user can act on it. A skill directory that already exists is never written into — that is someone else's skill — and install says whose. A same-named skill in the *other* scope is the existing shadowing rule, so install warns once and the index keeps one winner.

**D11 — Supply chain: what is in, and what is not.** In: an exact id match (no search, no suggestions, no "did you mean"), the full identity printed before anything is written (id, exact version, source, SHA-512, size, contents, dependencies and their versions), the total content size every install prints — with a project install above 10 MiB warning once and naming its largest entries, because that content is committed — `--dry-run`, path confinement for every manifest path and archive entry, entry-count and expanded-size bounds checked before extraction, per-file hashes in the record, and `--verify` to prove the content later. Out: author and repository signature verification, provenance attestations, vulnerability scanning, an SBOM. The SHA-512 is transport integrity against the feed, not a statement about who wrote the package, and the docs and the command's own output say so.

There is no install prompt. Install runs no code, so its identity is printed rather than asked about, and the only question on this path is the existing project trust question — which now names the package it came from. A prompt on install would ask the user to approve something that has not happened yet, and the thing worth approving ("this code runs") is exactly what the load-time question already asks.

**D12 — Phase 1 refuses dependencies by name; phase 2 resolves them; the release ships only with phase 2.** The format, the reader, the download, the record, the content write, the trust integration and uninstall do not need the walk, so they land first, with a dependency-declaring package refused naming the dependency. The walk, the lock, the materialisation and the digest's lock input follow as their own phase. The spec states the end state — a package may declare dependencies and they are resolved — because that is what the format promises; the tasks carry the split, and the change does not ship until phase 2 is in.

## Phase 6 amendments (`adr/0017-addendum-packages.md`)

Four behaviour changes were settled after the review of phases 1–5 and are folded into D7, D8 and D11 above; the tasks carry them as 6.8–6.11.

- **A session gates its own installs.** The bash tool adds `LUNATE_SESSION=1` to every child it starts, additively, through `ProcessRunRequest.ExtraEnvironment` — `Environment` still replaces, so an extension's allow-list is never widened by this; the marker is one extra entry. `lunate install` reads it and refuses without `--yes` (exit 2): code installed from a command runs at the next load, and that has to be visible in the command the approval panel showed. Outside a session the marker is absent and nothing changes. The approval panel already renders the command verbatim, which is what makes the flag part of what the user approved; a test pins it.
- **A broken record is recoverable.** An unreadable record no longer refuses an install: content and record are written as a replacement, and the run reports the bytes it could not account for (files under the content roots the new record does not name) instead of reporting a removal. `uninstall --force` removes what the package id alone names — the record's directory and `<root>/extensions/<package-id>/` — because the extension id and the skill directories are exactly what the record would have supplied. Without `--force` the refusal stands.
- **Size is reported, and a project install warns.** Every install prints `content: <size> in <n> files`, summed from the record (entry assembly, materialised dependencies, skills). A project install above 10 MiB warns once, naming the largest entries; the threshold and the human-readable format live in `PackageContentSize`, and the decision is pure so it is tested without a store.
- **Installing is not trusting, in either scope.** `TrustInventory` and `ExtensionLoader` gate a descriptor whose directory an install record owns, wherever it landed; the hand-placed user-scope extension stays trusted by placement. The decision uses the existing extensions section, keyed the way extension trust already is (repository identity and worktree), so no new record shape exists. The question's wording stops calling a user-scope install a project extension.

## Failure, offline and private feeds

Feeds and credentials come from NuGet's configuration chain rooted at the working directory, so a repository's `nuget.config` applies to a project install and the user's to a user install. No interactive credential prompt exists, here or in the single-file restore; a 401 names the source and the configuration file to fix.

`--offline` resolves the package and its dependency closure only from the global packages folder and makes no feed request at all, not even a version lookup. The test observes the feed seam and asserts zero calls, the same measurement the single-file suite already uses for a stale digest. A cold folder fails with the package, the missing dependency and the packages folder named, and writes nothing.

Every command is all-or-nothing in the sense that matters: no content exists that no record names. `--force` repairs a partial or hand-edited install by rewriting the recorded paths from the recorded version's nupkg and lock, which is also the answer to "the copy is broken, I do not want to upgrade".

## Risks

- **A user-scope install is one command away from running code.** That is already true of anything dropped in `<root>/extensions/`. Mitigations: the identity is printed in full, nothing updates itself, `--dry-run` exists, and project scope asks before use. Signature verification is the real fix and is deferred.
- **The walk makes install slower and can fail on a feed hiccup.** Accepted: it is the same machinery a single-file restore already runs, and an install is an explicit command, not a session start.
- **A package can pin a dependency the host would rather it did not** (an old `Newtonsoft.Json`, say). Mitigated by the graph being recorded and printed, and by the host-owned exclusion; a per-package dependency policy is deferred.
- **The copied files duplicate bytes already in the packages folder.** Accepted: the extension directory is the format the loader documents, a copy is what makes uninstall exact, and a published extension's payload is small next to the nupkg cache.
- **A bundle can ship an assembly the host owns.** Handled by the existing load rule (host copy wins, one warning) rather than by a new install refusal, so a package that makes the mistake still works.
- **Refusing cross-scope duplicates will annoy someone** with a user-level favourite and a project-level pin. The message names the install to remove; real precedence is a deferred decision, not an accident of scan order.
- **A package can be large enough to hurt.** Bounds are checked before extraction; there is no streaming or resumable download in v1.
- **A crash between the content write and the record write leaves files no record names.** The window is small and safe — untrusted, unrecorded content cannot load in a project — but it is real. The next install of that package replaces it, and `--force` repairs the recorded case; deleting directories no record names is deliberately not automated.

## Deferred

Marketplace and search; author signing and provenance; git URLs (a clone would need a commit pin, its own auth story and either a build toolchain or prebuilt content — the last is the only version worth building, and it is the same layout with a different fetch, so it is its own change); a separate `update` command with latest-stable resolution and `--all`; `templates` and `themes` as installable kinds (the registry reserves the slot and no template source ships); a committed `lunate.packages.json` that install reads; in-session install (`/install`); automatic updates; `--source` to pick a feed; RID-specific asset selection beyond what the walk already does; cache eviction of installed package locks; self-update of Lunate; distributing subagents as packages.

## Settled at sign-off

1. **git URLs** — refused by name in v1; the shape is clone-at-commit, same manifest, prebuilt content, and it is its own change.
2. **Manifest** — a separate `lunate.package.json`, because a skills-only bundle has no runtime to declare.
3. **Command surface** — `install`, `uninstall`, `packages`; a noun for the list, like `extensions` and `cache`.
4. **Provenance in the trust question** — yes: one line, `from package <id>@<version> (<source>)`, for an extension or a skill that came from an install. Provenance also stays in `lunate packages` and the record.
5. **Installed skills** — plain scope skills; `/skills` shows `from <package>@<version>` as provenance, and uninstall removes them through the record.
6. **Dependencies** — allowed, and resolved through the existing pipeline (D3–D5); phase 1 refuses them by name, phase 2 lands the walk, and the release waits for phase 2 (D12).
7. **Record and lock** — both: `install.json` for the install, the existing `extension.lock.json` for the graph.
8. **Default scope** — user; `--project` explicit, and a project install writes committable content with a local record (D6).
9. **Signature verification** — before anything marketplace-shaped ships, not in this change.
