# Remote command invocation

## Why

A remote extension's manifest commands are **declared, not provided**. The composition lists them ("pipe" appears in `/help` under `extensions`), but invoking one cannot work: the wire has no method for it, so the registry has nothing to dispatch to. A user sees a command that exists in the listing and does nothing when typed — the worst of the three possible states, because it looks like a bug in the command rather than a missing capability.

The in-process path already has the whole semantic: source-qualified ids (`ext:<id>:<name>`), an invocation lease, cancellation, a bounded drain on unload, and a teaching notice on failure. What is missing is the wire.

## What changes

- **One new request method, `command/invoke`.** The host sends the borrowed command name (bare, without the `ext:` prefix), the argument remainder and the invocation's request id; the child replies with the result — output text, or a failure whose message becomes the notice. The payload is a **wire DTO owned by `Lunate.Extensibility.Remote`**, never a serialisation of the public `CommandInvocation`/`CommandResult`, so the wire stays free to change without moving the abstract surface.
- **Cancellation reuses `$/cancelRequest`.** One cancel path for hooks, service calls and commands; no second mechanism to keep in step.
- **The reference node extension implements one command**, so the mechanism is exercised by the conformance run and the real-host test rather than described.
- **Capability negotiation.** `initialize` carries `commands: true` when the child can serve invocations. A child that does not is reported as "declared, not provided" with a diagnostic naming the missing capability, and the command is **omitted from the listing** rather than offered and dead.

## Devil's advocate

- **A second dispatch path is a second place for ids to diverge.** Mitigated by making the host-side source delegate to the same `CommandSource` shape the in-process source uses, so ordering, duplicate handling and the id format come from the registry, not from the wire adapter.
- **Serialisation of a "result" is not one shape.** `CommandResult` carries output, an optional error and a disposition; the DTO mirrors exactly those fields and nothing else. If the abstract type grows a field, the DTO does not silently inherit it — the conformance run fails, which is the correct outcome.
- **Cancellation across the wire is where most protocols get it wrong.** Reusing `$/cancelRequest` with the invoke request id means the child must be able to cancel a *command* the same way it cancels a hook: the request id must reach the handler's token. The task list makes that an explicit red test rather than an assumption.
- **Do not add a capability negotiation scheme "for later".** One boolean field, one documented meaning, one test — not a feature matrix.

## Impact

- `Lunate.Extensibility.Remote`: the method constant, request/response DTOs, the host adapter, the child dispatch entry.
- `Lunate.Extensibility`: the remote command source becomes dispatchable (manifest commands are no longer marked declared-only).
- `samples/extensions/node-remote`: one command (`ping`-style) implemented in the reference extension.
- Specs: `extensions` gains the wire requirement and the listing rule for undeclared capability.
- Wire protocol version: **2** (a method added; a version-1 child is still readable, and its commands are reported as not provided).
- No change to `Lunate.Extensibility.Abstractions` — the public surface does not move.
- No session-format change.
