## Context

The command registry already treats a manifest `commands` entry as a declaration ("declared, not provided") and the remote host already carries hooks and service calls with a lease, cancellation and a bounded drain. `command/invoke` adds a third request kind to a wire that was built for exactly this shape; it does not introduce a new lifecycle.

## Decisions

1. **A wire DTO, not the public type.** `CommandInvocation`/`CommandResult` live in the abstractions package and are the extension-facing contract; the wire gets its own record with exactly the fields it needs (name, remainder, request id; output, failure message, disposition). Serialising the abstract types would make every future field a wire change and every wire quirk a contract.

2. **Cancellation rides `$/cancelRequest`.** One cancel path. The child keeps a map from request id to token source, which it must already keep for service calls; command invocations reuse it. A second method would be a second thing to forget on unload.

3. **The lease is the loader's, not the wire's.** The invocation goes through the same lease the in-process path takes, so unload semantics (cancel, notify, bounded wait) are inherited rather than re-implemented.

4. **Capability over version guessing.** `initialize` advertises `commands: true`. The host does not infer support from the protocol version: a child can speak version 2 and still not serve commands (a fork, a different SDK). The flag is the statement of intent, and the listing follows the flag, not the number.

5. **Protocol version 2, readable in both directions.** The host accepts a version-1 child (hooks and services only) and reports its declared commands as not provided. It does not send `command/invoke` to a child that did not advertise the capability, regardless of version.

6. **The reference extension serves a real command.** `ping` in the node sample returns a string that includes its arguments, so the conformance run and the real-host test both exercise the path with no doubles.

## Risks

- **Wire and abstract result drift.** Mitigated by mapping field by field in one place and by a conformance step asserting the reply round-trips through the abstract result unchanged.
- **Cancel arriving after completion.** The child maps a `$/cancelRequest` for an already-finished id to a no-op, exactly as the hook path does; a test pins it.
- **A child that advertises the capability and then fails to serve.** That is a failure notice naming the source-qualified id, not a silent no-op — the same teaching shape as in-process.

## Out of scope

Command *arguments* beyond the remainder string (no structured args), command-side progress reporting, and any change to how in-process commands dispatch.
