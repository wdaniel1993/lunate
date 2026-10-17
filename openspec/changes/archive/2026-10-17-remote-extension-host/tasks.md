# Tasks — remote extension host

## 1 Wire layer

- [x] 1.1 `RemoteWire` in `Lunate.Extensibility.Remote`: NDJSON framing (UTF-8, no BOM, no batches; non-JSON stdout line = protocol error; 16 MB line cap = protocol error, not OOM), correlation with separate id spaces per direction, concurrent requests, `$/cancelRequest` ↔ `CancellationToken` both ways, error mapping; stderr piped to the extension log, capped. Unit tests.

  Implementation note (design/code divergence, smallest-surface choice): the pinned transport reuses the core process capability, but `IProcessRunner.RunAsync` + `LineHandler` can only write in response to a child line — a host-initiated protocol (`initialize` first) cannot be written through it. The capability gained one additive method, `IProcessRunner.StartSessionAsync(ProcessSessionRequest)` returning `IProcessSession` (bounded line reads, flushed line writes, `WaitForExitAsync`, `KillAsync`); `ProcessRunner` implements it and reuses the existing children-first/Job Object tree kill unchanged. `RunAsync` is untouched.
- [x] 1.2 Conformance inventory: every hook payload/result DTO + the service-call envelope, each round-tripped through the framing on both sides (extend the `HookContractTests` inventory so a new DTO cannot ship without a wire entry).
- [x] 1.3 JSON Schema generated from the hook/service DTOs; published with the docs and shipped in `Lunate.Extensibility.Testing`.

## 2 Remote extension adapter

- [x] 2.1 Manifest `runtime` parsing + validation (`entryAssembly` optional; exactly one of the two; `env` / `envPassthrough`; field-named errors); `PublicAPI` updates; tests.
- [x] 2.2 Environment scrubber: promote the code-mode sample's implementation into the contract assembly (minimal base set + passthrough + env); the sample switches to it; tests.
- [x] 2.3 `RemoteExtension` adapter: declaration-driven proxies (no start), lazy start on first use + `background` mode (after the UI is ready, never on the startup path), `initialize` (protocolVersion + capabilities, manifest echo, workspace; mismatch = refusal; result must match the manifest — undeclared refused, missing warned), hook bridging (subscribed-only; observers as notifications, interceptors as requests; per-class timeout mapping; gating hooks fail closed; RunId/ParentRunId/workspace on every call); tests including the reentrant case.
- [x] 2.4 Service bridging: consumption (`service/<name>/<method>` child → host) and registration (host → child; async-only); tests.
- [x] 2.5 Lifecycle: crash → in-flight calls fail immediately + one notice + at most one automatic restart + manual restart from `/extensions`; session end → shutdown, bounded grace, tree kill (Job Object on Windows, POSIX walk on Unix); tests on all three CI legs.

## 3 Composition

- [x] 3.1 Loader/composition accepts both runtime kinds; failed remote extensions contained like in-process ones; `--no-extensions` unaffected; trust digest = manifest + extension directory contents; trust prompt text states user privileges; tests.
- [x] 3.2 Node reference extension (manifest + script) as a fitness-suite member (`samples/extensions/`, tests in CI); production composition test through the real host (hook delivered, service call answered).
- [x] 3.3 `Lunate.Extensibility.Testing`: the conformance runner pointed at an arbitrary executable.

## 4 Close-out

- [x] 4.1 Spec deltas (`extensions`) + `adr/0017-addendum-remote-host.md` + docs (extension authoring page: remote manifest and wire reference incl. the JSON Schema).
- [ ] 4.2 Full gate (`scripts/verify.sh`, both cultures), adversarial review, archive, PR.
