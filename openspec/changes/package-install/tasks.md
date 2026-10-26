## 1. The package format and its reader (PR 1, no network)

- [x] 1.1 Red: a nupkg carrying `lunate.package.json` with `extension` and `skills` parses into a model that names both folders, in the order the manifest declares them.
- [x] 1.2 Red: a nupkg without `lunate.package.json` is refused, naming the package and what the file is for; a manifest with no declarable kind is refused naming both.
- [x] 1.3 Red: a manifest naming `templates` or `themes` is refused, naming the kind and saying this version does not install it.
- [x] 1.4 Red: every manifest path and every archive entry is confined to the package root — a rooted path, a `..` segment and an entry that resolves outside are each refused, naming the entry.
- [x] 1.5 Red: the `extension` folder must hold an `extension.json` the existing parser accepts, whose `apiVersion` range this host meets (`requires Lunate ≥ x.y` when above), and whose `entryAssembly` exists in the folder.
- [x] 1.6 Red: each skill folder must hold a `SKILL.md` whose frontmatter parses and whose name equals the directory; a malformed one is refused with the reason, and two skills with one name are refused.
- [x] 1.7 Red: in this phase a package that declares a NuGet dependency is refused, naming the dependency and saying that dependency-declaring packages arrive in phase 4; a package carrying `build/` or `buildTransitive/` installs and names the ignored folder once.
- [x] 1.8 Red: entry count above 20,000 or expanded size above 512 MiB is refused before extraction, naming the limit and the observed value.
- [x] 1.9 `--dry-run` reads the archive and prints the identity and contents, writing nothing.
- [x] 1.10 Gate: the new test classes pass, `dotnet csharpier check .` is clean, `openspec validate package-install --strict` is green, and a fixture nupkg built by the test helper parses from a local folder feed.

## 2. Download, record and verify (PR 2)

- [x] 2.1 `NuGetRestoreEngine`'s assembly gains the download (an exact id and version through `NuGetPackageFeed`, returning the nupkg path and the SHA-512 NuGet computed) — with the bundle reader, the record and the installer beside it. No reference pack, no extension lock in this phase, no compile, and nothing new in `Lunate.Extensibility` referencing `NuGet.*`.
- [x] 2.2 Red: an exact version installs from a local folder feed; the nupkg is in the packages folder; the record's `package` half carries the sources, canonical id, version, SHA-512 and the archive file list.
- [x] 2.3 Red: the record's `content` half lists every written path with size and SHA-256, and the record's integrity hash answers for its canonical bytes; re-reading and re-writing the record produces identical bytes.
- [x] 2.4 Red: a record edited by hand reads `broken`, and `lunate packages` and `uninstall` refuse to act on it, naming the file.
- [x] 2.5 Red: the same version installed twice reports already-installed, exits 0, and moves no file's modification time.
- [x] 2.6 Red: `--offline` with the nupkg already in the packages folder completes and the feed observer records zero requests; with a cold folder it fails naming the package and the folder, and writes nothing.
- [x] 2.7 Red: a download, verification or extraction failure leaves no content and no record; a second run succeeds; staging is gone.
- [x] 2.8 `lunate packages`, `--json` and `--verify` — the line shape, the ordering by scope then id, `packages: none installed`, `[]`, and exit 3 when a record fails.
- [x] 2.9 Gate: local-feed tests pass, the zero-request test is in the suite permanently (the same shape as the stale-digest test), `lunate packages --json` on a fixture store matches the documented object, and `scripts/verify.sh` passes — 5,550 tests per culture pass with 0 failures, startup floor 59 ms (median 60, budget 150).

## 3. Content, scope and trust integration (PR 3)

- [x] 3.1 Red: a user install lands `extensions/<id>/` and `skills/<name>/` under `LUNATE_HOME`, and a session loads the extension with no question asked.
- [x] 3.2 Red: a project install lands the extension under `<worktree>/.lunate/extensions/` and the skills under `<worktree>/.agents/skills/`, while the record stays under the state root and names the worktree; the files show up as untracked in `git status`.
- [x] 3.3 Red: a second worktree's `lunate packages` does not list the first worktree's project install, and a session there discovers no project content from it.
- [x] 3.4 Red: `lunate trust` lists a project install's pending extension and skills decisions with the source package, and `lunate trust --yes` records them, after which a session asks nothing.
- [x] 3.5 Red: the extension question for a project install carries the line `from package <id>@<version> (<source>)` (a requirement delta in `extensions`), the project skills question carries the same line (a delta in `skills`), and `/skills` shows `from <package>@<version>` for an installed skill.
- [x] 3.6 Red: a marker written by a bundle's factory is absent after install — no assembly was loaded and no factory ran.
- [x] 3.7 Red: `<root>/skills` is unchanged by a project install, `~/.agents/skills` is unchanged by a user install, and `lunate cache prune` leaves an install alone.
- [x] 3.8 Red: a duplicate extension id — against another install, a hand-placed packaged extension, or a single-file extension — is refused at install, naming both providers and the uninstall that clears one.
- [x] 3.9 Red: an existing skill directory is never written into, and a same-named skill in the other scope warns once and follows the existing shadowing rule.
- [x] 3.10 Gate: the trust tests never prompt (an unscripted prompt throws), discovery shows the installed extension with its scope, and `scripts/verify.sh` passes — 5,607 tests per culture, 0 failures; startup floor 58 ms (median 59, budget 150 ms); public API clean.

## 4. Dependencies: the walk, the lock and the digest (PR 4 — the release waits for this phase)

- [x] 4.1 The installer feeds the package's declared dependency set to the existing restore walker — same resolver, conformance corpus, packages folder and lock writer — and materialises the resolved assemblies beside the entry assembly. No new resolver, no compile, no build-cache write.
- [x] 4.2 Red: a package declaring `Octokit` installs against a local feed; `Octokit` and its own dependencies are materialised, and the lock at `<root>/cache/extensions/<extension-id>/extension.lock.json` names the exact versions in the existing graph/toolset shape.
- [x] 4.3 Red: host-owned assemblies are excluded from the lock's graph, and a package shipping a copy of the contract still installs and loads the host's copy.
- [x] 4.4 Red: a dependency no feed serves, and a graph the resolver cannot settle, each refuse the install naming the dependency and the feeds tried, with no content and no record written.
- [x] 4.5 Red: `--offline` resolves the entire closure from the packages folder with zero feed requests, and fails naming the missing dependency when one is absent.
- [x] 4.6 Red: the phase-1 refusal is gone (the package from 1.7 now installs) and the lock's canonical graph is part of the project trust digest, so a package change re-prompts.
- [x] 4.7 Red: `lunate cache prune` leaves an installed package's lock and content alone.
- [x] 4.8 Install prints the dependency closure with each resolved version before writing, and `--dry-run` prints the same without resolving into the store.
- [ ] 4.9 Gate: the walk's tests reuse the phase-2a fixtures where they fit, the zero-request test covers the closure, and `scripts/verify.sh` passes. The change does not ship before this phase is merged.

## 5. Upgrade and uninstall (PR 5)

- [ ] 5.1 Red: `lunate install <id>@<newer>` on an installed package removes the old content through the old record, writes the new content and records the new version; no file from the old version remains.
- [ ] 5.2 Red: an upgrade that fails while downloading or resolving leaves the installed content and the record byte-identical.
- [ ] 5.3 Red: `lunate install <id>@<older>` downgrades and records it.
- [ ] 5.4 Red: `uninstall` deletes exactly the recorded paths (extension, skills, record), leaves the nupkgs and the lock, and leaves a hand-added file while naming it.
- [ ] 5.5 Red: an edited recorded file refuses uninstall without `--force` and is removed with it; `--force` on install repairs a missing file from the recorded version.
- [ ] 5.6 Red: `uninstall` accepts an extension id when exactly one record names it and refuses an ambiguous one, naming the candidates.
- [ ] 5.7 `lunate --help` gains `install`, `uninstall` and `packages` in the same shape as `extensions` and `trust`, one usage line and one description line each.
- [ ] 5.8 Gate: upgrade and uninstall fixtures pass, a record diff proves the failed-upgrade case wrote nothing, `lunate --help` lists the three commands, and `scripts/verify.sh` passes.

## 6. Docs, the ADR addendum and the end-to-end run (PR 6)

- [ ] 6.1 `adr/0017-addendum-packages.md`: the bundle format, dependency resolution through the existing pipeline, the trust digest, the record/lock split, a project install's committable content with a local record, and the deferred list. Immutable once accepted, like every ADR.
- [ ] 6.2 `docs/extensions/packages.md`: the manifest reference, the folder layout, dependency resolution and its lock, the two scopes (what a project install commits and what it leaves in the state root), the record, what each command prints and exits with, offline and private feeds, and the trust sentence. Add its row to `docs/README.md` and link it from `single-file.md` and `manifest.md`.
- [ ] 6.3 `README.md` and `docs/guide.md`: one paragraph on install — what a package is, the two scopes, that a project install commits content and keeps its record locally, and that it is trusted through the existing question.
- [ ] 6.4 Red: an end-to-end run on a fixture feed — install a bundle with one tool, one skill and one dependency, start a session, see the tool declared and the skill listed with its provenance, `lunate packages --verify` exit 0, `uninstall`, and see all of it gone. The same run with `--project` prompts once (with the `from package …` line) and loads after approval.
- [ ] 6.5 No new NuGet package: the ADR-0017 set covers the download, the walk and the archive reader. If something else looks necessary, stop and ask rather than adding it.
- [ ] 6.6 Public API: new public types appear in `PublicAPI.Unshipped.txt`; nothing existing changes shape.
- [ ] 6.7 Gate: `scripts/verify.sh` green, `openspec validate package-install --strict` green, docs lint green, and the end-to-end run recorded with its output in the change's notes.
