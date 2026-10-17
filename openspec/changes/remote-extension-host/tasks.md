# Tasks — remote extension host

## 1 Wire layer

- [ ] 1.1 `RemoteWire` in `Lunate.Extensibility.Remote`: NDJSON framing, correlation (ids, result, error, notifications), `$/cancel`, unknown/duplicate id rejection, malformed-line handling; stdout protocol-only, stderr piped to the extension log. Unit tests.
- [ ] 1.2 Conformance inventory: every hook payload/result DTO + the service-call envelope, each round-tripped through the framing on both sides (extend the `HookContractTests` inventory so a new DTO cannot ship without a wire entry).

## 2 Remote extension adapter

- [ ] 2.1 Manifest `runtime` parsing + validation (`entryAssembly` optional; exactly one of the two; field-named errors); `PublicAPI` updates; tests.
- [ ] 2.2 `RemoteExtension` adapter: lazy start on first use, `initialize` handshake (v1 pinned, manifest echo, capabilities), hook bridging for the whole catalogue, failure mapping per hook policy; tests.
- [ ] 2.3 Service bridging: consumption (`service/<name>/<method>` child → host) and registration (host → child calls on the remote extension's services); tests.
- [ ] 2.4 Lifecycle: crash → failed status `the extension process exited` + session continues; session end → shutdown request, bounded grace, tree kill children-first; environment allow-list (no inheritance); tests.

## 3 Composition

- [ ] 3.1 Loader/composition accepts both runtime kinds; a failed remote extension is contained like a failed in-process one; `--no-extensions` unaffected; tests.
- [ ] 3.2 Scripted node reference extension (manifest + script) driven through the real composition path in CI.

## 4 Close-out

- [ ] 4.1 Spec deltas (`extensions`) + `adr/0017-addendum-remote-host.md` + docs (extension authoring page: remote manifest and wire reference).
- [ ] 4.2 Full gate (`scripts/verify.sh`, both cultures), adversarial review, archive, PR.
