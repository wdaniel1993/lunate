## 1. Sign-off and contract notes

- [ ] 1.1 Resolve the four OPEN QUESTIONS in `design.md` (handler boundary, unload semantics, default help coverage, shared typed namespace) and fold the answers in as `SIGN-OFF` notes before any `src/` work starts.
- [ ] 1.2 Land the addendum text from `adr.md` as `adr/0017-addendum-commands.md` (status accepted, dated on sign-off) and record the change reference beside the shipped `2026-10-23-command-source`.
- [ ] 1.3 Public API note, verified rather than assumed: this change adds **no** type or member to `Lunate.Extensibility.Abstractions`, so `src/Lunate.Extensibility.Abstractions/PublicAPI.Unshipped.txt` is untouched and `ExtensionApi.Current` stays `1.11.0`. If any task below turns out to need a public type, stop and re-decide — the design's whole shape is that ids and leases are host internals.

## 2. Help reads the whole registry

- [ ] 2.1 Red tests in `tests/Lunate.Coding.Tests`: (a) a registry with a built-in, a skill and an extension command prints the built-in groups first, unchanged, then a `skills` section and an `extensions` section with those rows; (b) an empty source (no skills, no extension commands) prints exactly today's listing, byte for byte; (c) the `/skill:<name>` placeholder stays in the built-in `extensions` group and is not repeated in the `skills` section.
- [ ] 2.2 Implement the sections in `src/Lunate.Coding/HelpView.cs` — signature unchanged, built-in path untouched, sections ordered by kind rank, rows ordinal by name, empty sections omitted.
- [ ] 2.3 Extend the existing `/help` end-to-end assertion (`tests/Lunate.Coding.Tests/InteractiveSessionCommandTests.cs`) with one non-built-in command present; keep the current built-in block assertion intact so the golden's `Assert.Contains` guarantee is preserved.

## 3. Command identity

- [ ] 3.1 Red tests in `tests/Lunate.Coding.Tests`: `CommandId.For` maps each kind to `builtin:`, `skill:`, `template:`, `ext:<id>:`; two extension sources offering `ping` compose to distinct ids and one dispatchable entry; the duplicate diagnostic names both ids; a handler failure notice names the id and the recovery step and contains no stack trace.
- [ ] 3.2 Add the internal `CommandId` helper in `src/Lunate.Coding` and thread the id through `CommandRegistry`'s candidate and entry types, computing it before the duplicate check.
- [ ] 3.3 Replace the duplicate and failure diagnostics in `src/Lunate.Coding/CommandRegistry.cs` with id-bearing text; route the session's error notice path (`InteractiveSession.DispatchCommandAsync`, built-in handler failures) through the same wording.
- [ ] 3.4 Red test in `tests/Lunate.Extensibility.Tests`: the loader's duplicate-provider warning names command ids, not only source ids; the load-time stamping still yields `Kind = Extension` and the extension id as source.

## 4. Invocation lifetime

- [ ] 4.1 Red tests in `tests/Lunate.Extensibility.Tests`: a handler awaiting its token is cancelled when its extension unloads; the drain completes and the unload returns; a handler that swallows its token is logged once and the unload still returns inside the bound; an idle extension unloads without touching the lease.
- [ ] 4.2 Red test in `tests/Lunate.Coding.Tests`: unloading an extension mid-command produces a notice naming the extension and the command id, and later commands still work.
- [ ] 4.3 Add `CommandInvocationLeases` and `LeasedCommandSource` in `src/Lunate.Extensibility`; wrap at `CommandSourceRegistry.Register` so no caller can obtain an unleased extension source.
- [ ] 4.4 Fix registration matching: `SetDescriptors` must identify its registration by extension id plus inner source, not by `ReferenceEquals` on the wrapper (otherwise stamped descriptors land on nothing and extension commands silently lose their descriptions).
- [ ] 4.5 Add `CommandSourceRegistry.CancelInFlightAsync(id, bound, ct)`; await it in `ExtensionLoader.Unload` before `context.Unload()`. Keep `Unregister` synchronous for the three load-failure cleanup sites, where no invocation can be in flight, and say so in a comment.

## 5. Template slot

- [ ] 5.1 Red test in `tests/Lunate.Coding.Tests`: a fixed template-kind test source composes last, appears under `templates` in `/help`, carries a `template:<name>` id, and loses a `help` collision to `builtin:help` with the diagnostic naming both ids.
- [ ] 5.2 No production template code. Confirm that the change added no discovery roots, no file format and no `/help` template content — the requirement is about the slot only.

## 6. Documentation and gate

- [ ] 6.1 `docs/spec/extensibility.md`: extend the Commands section with help coverage, ids, teaching errors, the invocation lease and the template slot; keep it a description of shipped behaviour, not a promise.
- [ ] 6.2 `docs/guide.md`: `/help` now lists skills and extension commands; one sentence on what happens to a running command when its extension is unloaded.
- [ ] 6.3 Focused suites: `dotnet test tests/Lunate.Coding.Tests --filter "FullyQualifiedName~Command|FullyQualifiedName~Help"`, `dotnet test tests/Lunate.Extensibility.Tests --filter "FullyQualifiedName~Command|FullyQualifiedName~Unload"`, plus the layering test.
- [ ] 6.4 Fast gate: `dotnet tool restore`, `dotnet csharpier check .`, `openspec validate command-source --strict`, `dotnet build lunate.sln -c Release`.
- [ ] 6.5 Layering proof: `tests/Lunate.Coding.Tests/LayeringChecker.cs` is unchanged — no `(Lunate.Coding, Lunate.Extensibility.Abstractions)` pair is added, because no project reference is added. Assert the layering suite is green with the file untouched.
