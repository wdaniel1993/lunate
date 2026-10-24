## 1. Wire

- [ ] 1.1 Red: `command/invoke` request round-trips naming the bare command and the remainder.
- [ ] 1.2 Red: a `$/cancelRequest` with the invoke id cancels the handler's token and replies `cancelled`.
- [ ] 1.3 Red: a cancel for a finished invoke id is a no-op, not an error.
- [ ] 1.4 `CommandInvokeMethod` constant, request/response DTOs and codecs beside the hook contracts; protocol version 2.

## 2. Child side

- [ ] 2.1 Map invoke request ids onto the existing cancel-token map used by service calls.
- [ ] 2.2 Dispatch to the registered command handler; failure becomes a failed reply carrying the message.
- [ ] 2.3 Advertise `commands: true` in `initialize`.

## 3. Host side

- [ ] 3.1 Red: a manifest command with an advertised child dispatches over the wire and its output reaches the transcript.
- [ ] 3.2 Red: a failing remote handler produces the in-process notice shape naming `ext:<id>:<name>`.
- [ ] 3.3 Red: unloading during an invocation cancels it, notifies, and completes within the bound.
- [ ] 3.4 A child without the capability: listing reports `declared, not provided`, autocomplete and dispatch do not offer it.

## 4. Reference extension and conformance

- [ ] 4.1 `ping` command in the node sample returning its arguments.
- [ ] 4.2 Conformance steps: command invoke, cancel mid-invoke, failure, capability-absent listing.
- [ ] 4.3 Production composition test through the real host.

## 5. Gate

- [ ] 5.1 Docs: remote extension page and `docs/spec/` wire table.
- [ ] 5.2 `openspec validate --strict`; csharpier; Release build; focused suites; full `scripts/verify.sh`; `ci-ok`; archive as the final commit of the same PR.
