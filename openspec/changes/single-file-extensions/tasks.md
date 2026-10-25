## 1. Discovery and the declaration (PR 1)

- [x] 1.1 Red: a `.cs` file under `.lunate/extensions/` with a valid header appears in `/extensions` as `not-built`, and nothing compiles.
- [x] 1.2 Red: a file with no `#:extension` is listed `invalid (no #:extension)`.
- [x] 1.3 Red: two files declaring the same id are both refused, naming both paths.
- [x] 1.4 The directive scanner: `#:extension`, `#:tool`, `#:hook`, `#:package`; unknown `#:` lines warn and are ignored.
- [x] 1.5 Grammar tests copied from the documented .NET 10 file-based-app syntax, including version ranges.
- [x] 1.6 Red: `#:sdk`, `#:property`, `#:project` are refused, each naming its directive.
- [x] 1.7 Red: an undeclared registration refuses the load; a declared-but-missing one warns and shows `declared, not provided`.
- [x] 1.8 The header mirrors every manifest declaration field (`#:command`, `#:service`, `#:capability`, `#:provider`); a test fails when the manifest grows a field with no directive.
- [x] 1.9 Red: only the leading block is scanned — a `#:` line after the first C# token is an error naming its line.
- [x] 1.10 No sidecar manifest for a single file; the header is the only declaration source.
- [x] 1.11 `/extensions` shows `not-built`, `invalid`, `stale`, `failed (trust)` with the reason on the same line.
- [x] 1.12 Docs: authoring page with the header grammar and the directive-to-manifest table.

## 2. Trust, restore, compile, cache (PR 2)

- [x] 2.1 Red: the digest covers id, packages, source bytes including the header, and lock bytes; editing any one of them changes it.
- [x] 2.2 Red: a stale digest stops before any restore and the test observes zero feed requests.
- [x] 2.3 Red: the trust prompt names the file, the packages, and that build-time analyzers and generators run.
- [x] 2.4 Red: no `NuGet.*` assembly is loaded in a session whose extensions are all built.
- [x] 2.5 In-process restore through the NuGet client libraries; packages resolve into `NUGET_PACKAGES` or the default global folder.
- [x] 2.6 `extension.lock.json`: exact versions, SHA-512, resolved assets; a restore that would change it reports and asks, never writes silently.
- [x] 2.7 Compile the file with the declared references; a failed compile reports the first diagnostic and leaves no build behind.
- [x] 2.8 Build cache keyed by source hash; last two builds kept; uninstall deletes the extension's builds; `lunate cache prune` removes orphans.
- [x] 2.9 Red: first trust → restore writes the lock and binds its hash into the trust record, with no second prompt; a compile afterwards uses that lock.
- [x] 2.10 Red: a hand-edited lock makes the extension `stale` and stops before any feed request.
- [x] 2.11 Reference pack: restore `Microsoft.NETCore.App.Ref` (runtime version, RID) through the same engine into the global packages folder on first compile; reuse it afterwards.
- [x] 2.12 Red: the published self-contained `osx-arm64` binary compiles a single-file extension with `HOME` empty and no `dotnet` on `PATH`; a second run makes no feed request.
- [x] 2.13 Generators and analyzers are off by default; `#:generators on` opts in and is part of the digest; `/extensions` shows `generators skipped: <package>` when a package ships them and the file did not opt in.
- [x] 2.14 Red: toggling `#:generators on` changes the digest and the prompt gains the build-time-code line.
- [x] 2.15 Report the measured per-extension cache size, the ref-pack size, the binary-size alternative if shipped, and the startup median after this phase.
- [x] 2.16 Docs: what gets restored and where, what the lock is for, how to read a compile failure, and that a Lunate update rebuilds extensions on first use because the cache key includes the API version.

### Phase-2a corrections (maintainer ruling)

- [x] 2a.1 The lock is two halves: a `graph` (sources, packages, exact versions, SHA-512, assets, edges) and a `toolset` (TFM, RID, runtime, reference pack, pack hash). Only the graph is in the trust digest (tag `lunate-extension-v2`); the source-only form keeps tag `lunate-extension-v1`, so an untouched file keeps its decision.
- [x] 2a.2 Red: the same source and the same graph under two toolset versions produce the same trust digest.
- [x] 2a.3 Red: a toolset change rebuilds the lock with **no** prompt — the prompt is never called (an unscripted prompt throws), the engine runs, and the trust record's digest and mtime do not move.
- [x] 2a.4 Red: a graph change still changes the digest and still asks; a stale digest still makes zero feed requests.
- [x] 2a.5 Red: a toolset block edited without its integrity hash is refused (the graph stays trusted) and the rebuild replaces it without asking.
- [x] 2a.6 The lock's canonical ordering and byte-identical output survive the split: a written lock parses and writes back identically, and two restores of the same source write the same bytes.
- [x] 2a.7 The lock lives in `~/.lunate/cache/extensions/<id>/extension.lock.json` for both scopes; the ADR addendum, the design note and the authoring docs say so, and say that a lock is produced per machine and determinism comes from the resolution rule, not from a committed file.

### Phase-2b maintainer additions (before merge)

- [x] 2b.1 Refuse a `LUNATE_HOME` that resolves inside the workspace or its repository root, with `LUNATE_HOME points inside the workspace; trust records there could be written by the repository`; real-path containment (symlinks, `.`/`..`, trailing separators, the platform's case rule, equal-to-root counts as inside).
- [x] 2b.2 Red: inside and equal-to-root refused at session composition, `lunate extensions build`, `lunate cache prune` and `lunate trust`; a sibling and an unrelated directory accepted.
- [x] 2b.3 Red: when `LUNATE_HOME` is set, `lunate --version` and the interactive welcome header both print `home <path> (LUNATE_HOME)`; neither prints it when unset.
- [x] 2b.4 Audit and document what follows the root and what does not; Lunate's own `~/.lunate/skills` was still resolving the profile and now follows the root; the docs state there are no log files.
- [x] 2b.5 Red: a source scan fails on any second profile-resolution site, and asserts `LUNATE_HOME` is read from the process environment only (nothing loads `.env`).
- [x] 2b.6 Red: a full session with `LUNATE_HOME` set and `HOME`/`USERPROFILE` pointing at a nonexistent directory exits successfully after a tool call and leaves that home nonexistent (child process).

## 3. Runtime: isolation, API gate, reload (PR 3)

- [x] 3.1 Red: a package shipping its own `Lunate.Extensibility.Abstractions.dll` loads against the host's copy and warns once.
- [x] 3.2 Red: an extension referencing an Abstractions above the host's `ExtensionApi` is refused with `requires Lunate ≥ x.y`.
- [x] 3.3 Red: `/extensions reload <id>` preflights trust, builds before touching the running extension, and leaves the old build loaded when the build fails.
- [x] 3.4 Red: a live invocation is cancelled with a notice and drained within the bound before the context disposes; a handler that ignores cancellation is logged once.
- [x] 3.5 Wire reload into the existing command leases; `ToolsChanged` after the new build loads.
- [x] 3.6 The template and docs show `PrivateAssets="all"` / `ExcludeAssets="runtime"`.

## 4. The loop, end to end (PR 4)

- [x] 4.1 Red: the model writes a single-file extension with one package reference, the user trusts it, `/extensions reload` makes its tool callable in the same session.
- [x] 4.2 Red: the trust refusal path — an untrusted file's tool is not callable and `/extensions` says why.
- [x] 4.3 A recorded end-to-end run in the suite using `ScriptedChatClient` and a built-in catalog model; verify under `HOME=/tmp/emptyhome`.
- [x] 4.4 Red: a model-initiated reload of a file it just edited ends at the trust prompt — the changed file is not compiled and the old build stays loaded.
- [ ] 4.5 README and guide: the single-file promise, with the real timing measured in phase 2.
