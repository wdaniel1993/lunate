# ADR review — command source completion

- Status: proposed — awaiting maintainer sign-off
- Review date: 2026-10-23

## Review Summary

The shipped command seam (`2026-10-23-command-source`) added a public extension surface — `ICommandSource`, `CommandDescriptor`, `CommandInvocation`, `CommandResult`, `IExtensionContext.RegisterCommandSource`, `ExtensionApi.Current` 1.11.0 — and its review recorded "None" under new durable ADRs on the grounds that the decision was change-local. That reasoning held while the surface was one commit old. It no longer holds: extension authors compile against it, and this change adds identity and lifetime rules that only make sense written against ADR-0017's principles. **This change therefore adds the ADR-0017 addendum the shipped surface never got.** It creates no new durable ADR: the registry is core policy, not a contract, and a durable ADR for "sort by kind" would over-record a decision that a future MCP-prompt source may legitimately revisit.

## In-Force ADRs Reviewed

- `adr/0017-extensibility-architecture.md` — principles 3 (JSON-serializable boundary), 4 (contract assembly only) and 6 (in-process extensions are fully trusted); the manifest-declaration and registration-only rules. The command surface follows them, with one exception named in the addendum (the source is a live in-proc interface, not a JSON payload).
- `adr/0002-layering-and-meai-model-types.md` — no new edge; `Lunate.Coding` reaches the Abstractions types transitively through `Lunate.Extensibility`, and `Lunate.Agent` gains no reference.
- `adr/0012-tool-contract.md` — the sibling capability seam. Commands copy its declared-vs-provided asymmetry, deterministic ordering and contained handler failure rather than inventing a second dispatch architecture.
- `adr/0017-addendum-skills.md` — the shape of a capability addendum (numbered decisions, consequences, explicit v1 boundary). `/skill:<name>` already routes through the same registry; this addendum must not contradict the skills addendum's invocation-as-a-user-turn rule.

## New Durable ADRs Created

- None. The addendum below lands as `adr/0017-addendum-commands.md` when this change is signed off.

---

# 0017 (addendum) — Slash commands: one source contract, one registry, identity and lifetime

- Status: proposed — 2026-10-23 (addendum to ADR 0017; command-source-completion sign-off)
- Date: 2026-10-23
- Relates to: ADR 0017 (extensibility architecture), ADR 0002 (layering), ADR 0012 (tool contract), ADR 0017 addendum — skills (`/skill:<name>` invocation)

## Context

ADR 0017 committed the core to primitives and left features to extensions, with extension capabilities crossing a semver'd contract assembly. Commands were the last frontend control surface outside that model: the built-in table, the completion registry and the dispatch switch were three parallel lists that agreed only because tests kept them aligned, and a manifest `commands` entry was advertised in autocomplete with no invocation path at all.

`2026-10-23-command-source` closed that: one `ICommandSource` contract in `Lunate.Extensibility.Abstractions`, one `CommandRegistry` in `Lunate.Coding`, ordered sources, first-source-wins, manifest-declare/provide enforcement, and contained handler failures. That change shipped without an addendum. This addendum records the surface as accepted extension API and fixes the two rules the shipped change left open: **identity** (what a command is called when two sources offer one name) and **lifetime** (what happens to a running command when its extension unloads).

## Decision

1. **One contract, one assembly, no Tui types.** Commands cross the extension boundary as `ICommandSource` plus three DTOs — `CommandDescriptor` (name, description, optional argument hint, source id, kind), `CommandInvocation` (name, argument remainder), `CommandResult` (at most one message, an error flag) — all in `Lunate.Extensibility.Abstractions`. Every extension-facing signature takes strings, enums and these records: never a `Lunate.Tui` type, never a `KeyEvent`, never a renderer handle. Parsing, key handling and notice rendering stay in `Lunate.Coding`.
2. **A live interface is the one deliberate exception to the JSON-only boundary.** ADR 0017 principle 3 says everything crossing the boundary is JSON-serializable "so out-of-process extensions in other languages stay possible". `ICommandSource` is a live in-proc object, and calling a handler in-proc is the point: a command is user-initiated frontend control with a result the session renders immediately, not a fire-and-forget event. The exception is bounded: the *payloads* (`CommandInvocation`, `CommandResult`) are JSON-serializable records, so the future out-of-process host adds a wire adapter over the same shapes instead of a second contract. An extension that needs a real process boundary today is out of scope and says so by not shipping a command.
3. **Registration is registration only.** A source is registered in `IExtensionFactory.Create` through `IExtensionContext.RegisterCommandSource`. Registration starts no process, opens no socket, sets no timer. Describing may read live state; it is called once per composition and cached at load. Costs that need lifetime belong to `SessionStarted` or a background service, as ADR 0017 already requires.
4. **The manifest is the declaration; code is the provision.** A command is runnable only when the manifest declares it *and* a source provides it. Provided-but-undeclared refuses the load. Declared-but-unprovided is logged, reported by `/extensions` as `declared, not provided`, and never autocompleted or dispatched. A provided name equal to a built-in refuses the load, naming the extension and the built-in. The rule is asymmetric on purpose: the manifest is what the user trusted before any code loaded.
5. **Ordering is fixed and kind-based.** Built-ins, then skills, then extension commands, then templates; within a kind, source registration order, then descriptor order. The first entry after that order owns a duplicate name. Kind is stamped by the host for extension descriptors, so an extension cannot promote itself into the built-in slot.
6. **Identity is source-qualified and host-owned.** Every command has one id: `builtin:<name>`, `skill:<name>`, `template:<name>`, `ext:<extension id>:<name>`. The id is computed by the host from the descriptor's kind, source and name; sources cannot set it. Ids appear in duplicate diagnostics, log lines and failure notices, and never in the typed command text or an autocomplete row. Two extensions may therefore offer `ping`: the bare name dispatches to the first after ordering, and every diagnostic can still say which one that was. A typed qualified alias is deliberately not introduced — it would add a second syntax to parse, complete and document for a case the log already answers.
7. **An invocation has a lifetime, and unload ends it.** An extension command invocation holds a lease on its owning extension. Unloading an extension cancels the tokens of its live invocations, tells the user (a notice naming the extension and the command id), and waits a bounded time for the handlers to return before the load context unloads; a handler that ignores cancellation is logged once and does not hold teardown open. The runtime keeps a load context alive while its code is on the stack, so this is a semantics rule, not a crash guard: an extension must not appear gone while its command is still mutating the host.
8. **Failure is contained and legible.** A handler exception becomes an error notice naming the command's id, its source, the reason and one recovery step (`/extensions` for an extension command, `/skills` for a skill), plus one log entry; never a stack trace in the scrollback and never a crashed session. Cancellation of the *caller's* token propagates unchanged. A source that throws while describing is logged and skipped for that composition.
9. **Help is the registry's view, not a second list.** `/help` prints the built-in groups first and unchanged, then one section per non-built-in kind that actually has commands. A session with no skills and no extension commands prints exactly the built-in listing. `/skills` keeps the scope-and-state detail; help lists names.
10. **No packages, no layering edge, no API churn beyond the contract.** The surface adds no NuGet package and no `ProjectReference`: `Lunate.Coding` consumes the Abstractions types transitively through `Lunate.Extensibility`, `Lunate.Agent` references neither, and any future direct edge must be declared in `LayeringChecker` first. The registry, ids and leases are host internals; a change that needs a new public type on this surface is a semver-minor `ExtensionApi` move, which neither the shipped change's follow-on nor this addendum makes.

## Consequences

- Extensions get a command surface that behaves like the tool surface: declared, provided, ordered, contained, and versioned by the contract assembly.
- `ext:<id>:<name>` makes the shadowing case observable without making it configurable. A user who wants a specific extension's `ping` disables the other extension; the log says which is live and why.
- Unload becomes a cooperative operation with a bound. A command handler that blocks forever is a logged bug, not a hung `/extensions` teardown.
- The out-of-process gap stays honest: remote extensions have no in-proc source, so every manifest command they declare reads `declared, not provided` rather than pretending to be runnable.
- A future template or MCP-prompt source joins as one more `ICommandSource` with an id prefix of its own; neither needs a new registry, a new dispatch path or a new help renderer.
