## 1. The package format and its reader (PR 1, no network)

- [ ] 1.1 Red: a nupkg carrying `lunate.package.json` with `extension` and `skills` parses into a model that names both folders, in the order the manifest declares them.
- [ ] 1.2 Red: a nupkg without `lunate.package.json` is refused, naming the package and what the file is for; a manifest with no declarable kind is refused naming both.
- [ ] 1.3 Red: a manifest naming `templates` or `themes` is refused, naming the kind and saying this version does not install it.
- [ ] 1.4 Red: every manifest path and every archive entry is confined to the package root — a rooted path, a `..` segment and an entry that resolves outside are each refused, naming the entry.
- [ ] 1.5 Red: the `extension` folder must hold an `extension.json` the existing parser accepts, whose `apiVersion` range this host meets (`requires Lunate ≥ x.y` when above), and whose `entryAssembly` exists in the folder.
- [ ] 1.6 Red: each skill folder must hold a `SKILL.md` whose frontmatter parses and whose name equals the directory; a malformed one is refused with the reason, and two skills with one name are refused.
- [ ] 1.7 Red: a package declaring a NuGet dependency is refused, naming the dependency; a package carrying `build/` or `buildTransitive/` installs and names the ignored folder once.
- [ ] 1.8 Red: entry count above 20,000 or expanded size above 512 MiB is refused before extraction, naming the limit and the observed value.
- [ ] 1.9 `--dry-run` reads the archive and prints the identity and contents, writing nothing.
- [ ] 1.10 Gate: the new test classes pass, `dotnet csharpier check .` is clean, `openspec validate package-install --strict` is green, and a fixture nupkg built by the test helper parses from a local folder feed.

## 2. Download, record and verify (PR 2)

- [ ] 2.1 `NuGetRestoreEngine`'s assembly gains one public method that resolves an exact id and version and downloads it through `NuGetPackageFeed`, returning the nupkg path and the SHA-512 NuGet computed — with the bundle reader, the record and the installer beside it. No dependency walk, no reference pack, no extension lock, and nothing new in `Lunate.Extensibility` referencing `NuGet.*`.
- [ ] 2.2 Red: an exact version installs from a local folder feed; the nupkg is in the packages folder; the record's `package` half carries the sources, canonical id, version, SHA-512 and the archive file list.
- [ ] 2.3 Red: the record's `content` half lists every written path with size and SHA-256, and the record's integrity hash answers for its canonical bytes; re-reading and re-writing the record produces identical bytes.
- [ ] 2.4 Red: a record edited by hand reads `broken`, and `lunate packages`, `uninstall` and `update` refuse to act on it, naming the file.
- [ ] 2.5 Red: the same version installed twice reports already-installed, exits 0, and moves no file's modification time.
- [ ] 2.6 Red: `--offline` with the nupkg already in the packages folder completes and the feed observer records zero requests; with a cold folder it fails naming the package and the folder, and writes nothing.
- [ ] 2.7 Red: a download, verification or extraction failure leaves no content and no record; a second run succeeds; staging is gone.
- [ ] 2.8 `lunate packages`, `--json` and `--verify` — the line shape, the ordering by scope then id, `packages: none installed`, `[]`, and exit 3 when a record fails.
- [ ] 2.9 Gate: local-feed tests pass, the zero-request test is in the suite permanently (the same shape as the stale-digest test), `lunate packages --json` on a fixture store matches the documented object, and `scripts/verify.sh` passes.

## 3. Content, scope and trust integration (PR 3)

- [ ] 3.1 Red: a user install lands `extensions/<id>/` and `skills/<name>/` under `LUNATE_HOME`, and a session loads the extension with no question asked.
- [ ] 3.2 Red: a project install lands under `<worktree>/.lunate/`, writes no trust record, and the next session asks the existing extension question (id, version, directory) and the existing project-skills question, with `trust.json` unchanged by the install itself.
- [ ] 3.3 Red: `lunate trust` lists a project install's pending decisions and `lunate trust --yes` records them, after which a session asks nothing.
- [ ] 3.4 Red: a marker written by a bundle's factory is absent after install — no assembly was loaded and no factory ran.
- [ ] 3.5 Red: `~/.agents/skills` and `<root>/cache/` are unchanged by either scope, and `lunate cache prune` leaves an install alone.
- [ ] 3.6 Red: a duplicate extension id — against another install, a hand-placed packaged extension, or a single-file extension — is refused at install, naming both providers and the uninstall that clears one.
- [ ] 3.7 Red: an existing skill directory is never written into, and a same-named skill in the other scope warns once and follows the existing shadowing rule.
- [ ] 3.8 Gate: the trust tests never prompt (an unscripted prompt throws), the discovery test shows the installed extension with its scope, and `scripts/verify.sh` passes.

## 4. Update, uninstall and the failure paths (PR 4)

- [ ] 4.1 Red: `lunate update <id>` moves 1.0.0 → 1.1.0, replaces the content and writes a new record whose hash answers for the new bytes.
- [ ] 4.2 Red: an update that fails while downloading leaves the installed content and the record byte-identical.
- [ ] 4.3 Red: `lunate update <id>@<version>` pins, including a downgrade; `--all` updates every package in the scope and reports one line each.
- [ ] 4.4 Red: a package the feed no longer serves fails `update` naming the package and the feeds tried, and removes nothing.
- [ ] 4.5 Red: `uninstall` deletes exactly the recorded paths and the record, leaves the nupkg in the packages folder, and leaves a hand-added file while naming it.
- [ ] 4.6 Red: an edited recorded file refuses uninstall without `--force` and is removed with it; `--force` on install repairs a missing file from the recorded version.
- [ ] 4.7 Red: `uninstall` accepts an extension id when exactly one record names it and refuses an ambiguous one, naming the candidates.
- [ ] 4.8 `lunate --help` gains `install`, `update`, `uninstall` and `packages` in the same shape as `extensions` and `trust`, one usage line and one description line each.
- [ ] 4.9 Gate: update and uninstall fixtures pass, a record diff proves the failed-update case wrote nothing, `lunate --help` lists the four commands, and `scripts/verify.sh` passes.

## 5. Docs, the ADR addendum and the end-to-end run (PR 5)

- [ ] 5.1 `adr/0017-addendum-packages.md`: the bundle format, download-not-restore, the self-contained rule, trust unchanged, the record, and the deferred list. Immutable once accepted, like every ADR.
- [ ] 5.2 `docs/extensions/packages.md`: the manifest reference, the folder layout, the two scopes, the record, what each command prints and exits with, offline and private feeds, and the trust sentence. Add its row to `docs/README.md` and link it from `single-file.md` and `manifest.md`.
- [ ] 5.3 `README.md` and `docs/guide.md`: one paragraph on install — what a package is, the two scopes, and that a project install is trusted through the existing question.
- [ ] 5.4 Red: an end-to-end run on a fixture feed — install a bundle with one tool and one skill, start a session, see the tool declared and the skill listed, `lunate packages --verify` exit 0, `uninstall`, and see both gone. The same run with `--project` prompts once and loads after approval.
- [ ] 5.5 No new NuGet package: the ADR-0017 set covers the download and the archive reader. If something else looks necessary, stop and ask rather than adding it.
- [ ] 5.6 Public API: new public types appear in `PublicAPI.Unshipped.txt`; nothing existing changes shape.
- [ ] 5.7 Gate: `scripts/verify.sh` green, `openspec validate package-install --strict` green, docs lint green, and the end-to-end run recorded with its output in the change's notes.
