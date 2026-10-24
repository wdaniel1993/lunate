## 1. Discovery and the declaration (PR 1)

- [ ] 1.1 Red: a `.cs` file under `.lunate/extensions/` with a valid header appears in `/extensions` as `not-built`, and nothing compiles.
- [ ] 1.2 Red: a file with no `#:extension` is listed `invalid (no #:extension)`.
- [ ] 1.3 Red: two files declaring the same id are both refused, naming both paths.
- [ ] 1.4 The directive scanner: `#:extension`, `#:tool`, `#:hook`, `#:package`; unknown `#:` lines warn and are ignored.
- [ ] 1.5 Grammar tests copied from the documented .NET 10 file-based-app syntax, including version ranges.
- [ ] 1.6 Red: `#:sdk`, `#:property`, `#:project` are refused, each naming its directive.
- [ ] 1.7 Red: an undeclared registration refuses the load; a declared-but-missing one warns and shows `declared, not provided`.
- [ ] 1.8 Optional sidecar JSON for single files; a header always wins and a conflict is refused by name.
- [ ] 1.9 `/extensions` shows `not-built`, `invalid`, `stale`, `failed (trust)` with the reason on the same line.
- [ ] 1.10 Docs: authoring page with the header grammar and the optional sidecar.

## 2. Trust, restore, compile, cache (PR 2)

- [ ] 2.1 Red: the digest covers id, packages, source bytes including the header, and lock bytes; editing any one of them changes it.
- [ ] 2.2 Red: a stale digest stops before any restore and the test observes zero feed requests.
- [ ] 2.3 Red: the trust prompt names the file, the packages, and that build-time analyzers and generators run.
- [ ] 2.4 Red: no `NuGet.*` assembly is loaded in a session whose extensions are all built.
- [ ] 2.5 In-process restore through the NuGet client libraries; packages resolve into `NUGET_PACKAGES` or the default global folder.
- [ ] 2.6 `extension.lock.json`: exact versions, SHA-512, resolved assets; a restore that would change it reports and asks, never writes silently.
- [ ] 2.7 Compile the file with the declared references; a failed compile reports the first diagnostic and leaves no build behind.
- [ ] 2.8 Build cache keyed by source hash; last two builds kept; uninstall deletes the extension's builds; `lunate cache prune` removes orphans.
- [ ] 2.9 Report the measured per-extension cache size and the startup median after this phase.
- [ ] 2.10 Docs: what gets restored and where, what the lock is for, how to read a compile failure.

## 3. Runtime: isolation, API gate, reload (PR 3)

- [ ] 3.1 Red: a package shipping its own `Lunate.Extensibility.Abstractions.dll` loads against the host's copy and warns once.
- [ ] 3.2 Red: an extension referencing an Abstractions above the host's `ExtensionApi` is refused with `requires Lunate ≥ x.y`.
- [ ] 3.3 Red: `/extensions reload <id>` preflights trust, builds before touching the running extension, and leaves the old build loaded when the build fails.
- [ ] 3.4 Red: a live invocation is cancelled with a notice and drained within the bound before the context disposes; a handler that ignores cancellation is logged once.
- [ ] 3.5 Wire reload into the existing command leases; `ToolsChanged` after the new build loads.
- [ ] 3.6 The template and docs show `PrivateAssets="all"` / `ExcludeAssets="runtime"`.

## 4. The loop, end to end (PR 4)

- [ ] 4.1 Red: the model writes a single-file extension with one package reference, the user trusts it, `/extensions reload` makes its tool callable in the same session.
- [ ] 4.2 Red: the trust refusal path — an untrusted file's tool is not callable and `/extensions` says why.
- [ ] 4.3 A recorded end-to-end run in the suite using `ScriptedChatClient` and a built-in catalog model; verify under `HOME=/tmp/emptyhome`.
- [ ] 4.4 README and guide: the single-file promise, with the real timing measured in phase 2.
