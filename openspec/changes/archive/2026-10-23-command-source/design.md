## Context

A submitted line beginning with `/` is frontend control: it bypasses the input pipeline and never reaches the model. Today built-in commands are described by `Commands.Descriptors`, dispatched by the switch in `InteractiveSession.DispatchCommandAsync`, and completed by `CompletionRegistry.Compose` — three paths that agree only because tests keep them aligned. The skill catalog is a fourth input to completion, and loaded extension manifests are a fifth. Manifest `commands` have no invocation path at all, so listing them in autocomplete is a promise the product cannot keep.

ADR-0017 makes the extension contract the place for extension capabilities and requires registration to be registration only. This change moves commands onto that contract without widening the trust boundary: an in-process extension is already fully trusted.

## Goals / Non-Goals

**Goals:**

- One ordered command source contract in the public abstractions, with async, cancellable describe and invoke.
- One registry in `Lunate.Coding` that is the sole input to autocomplete, dispatch and `/help`.
- Built-ins first with byte-identical user-visible behaviour.
- Skills behind the seam, `/skill:<name>` unchanged.
- Extension manifest commands that are either genuinely runnable or explicitly reported as not provided.
- Deterministic conflict handling and contained failures.

**Non-Goals:**

- A scripted prompt-template command source. The `Template` kind reserves its ordering slot; no template source ships in this change.
- Out-of-process command dispatch. Remote extensions keep manifest-only commands, which read as declared, not provided until a wire command contract exists.
- Changing command parsing, history, busy refusals, approval or session persistence.
- Adding command arguments to autocomplete rendering; `ArgumentHint` is carried for later use.

## Decisions

### The contract is four small DTOs and one interface

`CommandDescriptor` carries `Name` (bare, no slash), `Description`, nullable `ArgumentHint`, `SourceId` and `Kind`. `CommandInvocation` carries the bare name and the argument remainder. `CommandResult` carries at most one user-facing message and an error flag, so a source can either act through its own host callback (built-ins, skills) or return a notice (extensions). `ICommandSource` exposes `SourceId`, `DescribeAsync` and `InvokeAsync`; both are `ValueTask` and take a `CancellationToken`, matching the existing hook/service contract style.

A source may describe its commands from live state. The registry asks each source once per composition; a source that throws while describing is logged and skipped, so one broken extension cannot take down the command surface. Describe failures are not turned into per-command entries: the source contributed nothing.

### The registry sorts by kind, then source order, then descriptor order

Composition orders entries by a fixed rank — `Builtin` 0, `Skill` 1, `Extension` 2, `Template` 3 — then by source registration order, then by the source's own descriptor order. The kind rank is what makes "built-ins first" independent of who composes the list. Extension descriptors are re-stamped by the loader to `Kind = Extension` and `SourceId = <extension id>`, so an extension cannot promote itself into another slot.

Duplicate names keep the first entry after sorting and log one line naming both sources. This replaces the current completion-only first-wins rule with the same rule at the registry level; dispatch and autocomplete can no longer disagree.

### Built-ins keep their switch, inside the first source

`Commands.Descriptors` remains the built-in metadata table (name, help group, description). `BuiltinCommandSource` maps it to the public descriptors and owns the existing dispatch switch. The interactive session's `DispatchCommandAsync` no longer switches: it parses, looks the name up in the registry, invokes the owning source and renders any returned message. The switch is now the built-in source's implementation, not a second dispatch path. `HelpView` and autocomplete read the registry, and the existing table/switch guard test follows the switch to its new file.

The `/skill:` placeholder stays in the built-in table for `/help`. Real skills are descriptors from the skill source. An exact `skill:<name>` match dispatches to the skill source; an unknown `skill:<name>` falls back to the placeholder entry, whose built-in handler invokes the existing skill path and therefore keeps the `unknown skill: <name>` notice.

### Skills move behind the seam without changing their behaviour

`SkillCommandSource` describes every valid, trusted skill as `skill:<name>` with the skill description, and invokes the session's existing `InvokeSkillAsync`. Invalid and untrusted entries stay out of the descriptor list, matching today's completion filter. The source is composed after the skill index is built, so `/new` and resume recompose it with the new catalog.

### Extensions register a source; the manifest is the declaration

`IExtensionContext.RegisterCommandSource(ICommandSource)` is registration only. `ExtensionLoader` collects the sources per extension and, after the factory returns, asks each source to describe its commands:

- a provided name that the manifest does not declare refuses the load (the manifest is what the user trusted before code loaded);
- a declared name that no source provides is logged and recorded as `declared, not provided`; it is not autocompleted and does not dispatch;
- a provided name equal to a built-in name refuses the load naming the extension and the built-in;
- an invalid name (empty, whitespace, leading slash) refuses the load.

The built-in name list reaches the loader through `ExtensionHostOptions.ReservedCommands`, set by the production composition from `Commands.BuiltinNames`; `Lunate.Extensibility` stays independent of `Lunate.Coding`. The described descriptors are cached on the registration so composition does not call an extension's describe twice. Unload removes the sources and the cache with the extension.

Remote extensions have no in-process source, so every manifest command they declare is recorded as declared, not provided and logged.

### Failure and cancellation

The registry invokes the owning source inside a try/catch. An `OperationCanceledException` when the caller's token is cancelled propagates; any other exception is logged with the command and source id and returned as an error result, which the session renders as an error notice. This covers built-in, skill and extension handlers uniformly. Registration and describe do not start processes, sockets or timers.

### `/extensions` renders the gap

`ExtensionCounts` gains the declared-not-provided names for the loaded extension. The text form renders `commands <provided> (<declared> declared, not provided: <names>)`; the JSON form adds `commandsDeclaredNotProvided` only when non-empty. Counts for trust-denied and failed extensions stay the manifest's declared counts, unchanged.

### API version

The contract assembly gains public types and a context method; `ExtensionApi.Current` moves to `1.11.0` as a semver-minor addition. Existing `^1.x` manifests stay compatible.

## Risks / Trade-offs

- **The built-in switch still exists.** It is control flow inside the first source, not a parallel dispatcher; the production composition test and the table/switch guard keep it honest.
- **Describe is called during load.** That is the only point where the manifest can be enforced before the extension becomes visible. The result is cached, so composition does not repeat it; a slow describe counts against the extension's load time.
- **Duplicate names across extensions are silent-ish.** First source wins with one log line, consistent with MCP and completion behaviour. A user who expected the later extension to win sees the diagnostic in the log and can rename.
- **Remote commands are not runnable.** They are reported honestly as declared, not provided instead of being advertised. A remote command wire contract is a later change.
- **Public API churn.** Three DTOs and one interface add unshipped API lines; the version bump is intentional and required for extension authors.

## Migration Plan

No data or session migration. Built-in and skill commands keep their names, output and dispatch semantics. An extension that declared `commands` without providing them now logs a warning and shows `declared, not provided`; it must register a command source to become runnable. An extension that provides a command colliding with a built-in is refused at load and must rename it.

## Open Questions

None. The change is compatible with ADR-0017 (extension contract, registration-only, in-process trust) and does not require a new durable ADR.
