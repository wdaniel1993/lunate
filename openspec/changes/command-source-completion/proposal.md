## Why

The command source seam already shipped: `2026-10-23-command-source` (commit `21090d58`) put `ICommandSource` in `Lunate.Extensibility.Abstractions`, added `CommandRegistry` in `Lunate.Coding`, ordered built-ins → skills → extensions → templates, made extension manifest commands dispatch in-proc, and refuses a built-in collision at load. **This change does not re-propose any of that.** It closes the four places where the shipped seam is still thinner than the promise "one registry feeds autocomplete, dispatch and help".

1. **`/help` is not source-complete.** `HelpView.Lines` filters `descriptor.Kind is CommandKind.Builtin`, so a skill or an extension command that autocompletes and dispatches is undiscoverable from help. The registry is nominally the input to help; in practice help reads only its first kind. The shipped spec says so out loud — its scenario reads "`/help` reflects the registry's built-in entries".
2. **A command has no stable identity.** Dispatch keys on the typed token and the first source after ordering wins. The duplicate diagnostic names the two *sources*, not the two commands, so with extensions `alpha` and `beta` both offering `ping` the log cannot say which `ping` is live, and a handler failure reads `command /ping failed: …` with no way to tell whose failure it was.
3. **An invocation has no lifetime.** `ExtensionLoader.Unload` calls `_commandSources.Unregister(id)` and then `context.Unload()`, while a handler started from the session's registry snapshot may still be running. Nothing cancels it and the user is never told. The headless `lunate extensions` teardown is the path that reaches this today; a reload/restart path is one change away, and the semantics are undefined either way.
4. **The template slot is a rank and nothing else.** `CommandKind.Template` sorts last and no requirement states what a template source must obey — whether it may shadow a built-in, what its id is, whether it appears in help. P8 would have to invent a second seam to answer that.

A fifth gap is documentary: the shipped command surface is a public extension API with **no ADR-0017 addendum**. The archived change declined one ("the decision is change-local"); that was defensible while the surface was new, but the surface is now what extension authors compile against, and every other extension surface of comparable size has one (`0017-addendum-model-registry.md`, `-remote-host.md`, `-skills.md`).

## What Changes

- **`/help` becomes source-complete.** Built-in groups print first, byte-identical and in their existing order; then one section per non-built-in kind present in the registry — `skills`, `extensions`, `templates` — each sorted by name, each omitted when empty. The `/skill:<name>` placeholder stays in its built-in `extensions` group. A bare session with no skills and no extension commands prints exactly today's listing.
- **Every command gains a source-qualified id** — `builtin:<name>`, `skill:<name>`, `template:<name>`, `ext:<extension id>:<name>` — used by every diagnostic, log line and error notice. The id never changes the typed token or an autocomplete row; it is host-side identity, not a new user syntax.
- **Error notices become teaching notices.** A failing handler reports the command's id, its source, the reason and the recovery step (`/extensions` for an extension command, `/skills` for a skill), never a stack trace.
- **An invocation holds a lease on its extension.** Unloading an extension with a live invocation cancels that invocation's token, tells the user (a notice naming the extension and the command id), and waits a bounded time for the handler to return before the load context unloads. A handler that ignores cancellation is logged and does not block the unload indefinitely.
- **The template slot is specified, not built.** A `CommandKind.Template` source is required to compose under the existing rules, order after extension commands, describe ids as `template:<name>`, and to lose a name collision with a built-in. No template discovery ships; the slot is proven by a test source.
- **ADR-0017 addendum** for the command surface, carried in this change's `adr.md` and landing as `adr/0017-addendum-commands.md` on sign-off.

## What Does Not Change

- `ICommandSource`, `CommandDescriptor`, `CommandInvocation` and `CommandResult` — no new members, no new public types, **no `ExtensionApi.Current` bump**, no `PublicAPI.Unshipped.txt` lines.
- Built-in command names, `/help` groups, descriptions, ordering, busy-session refusals and notice levels; `/skill:<name>` wrapping and its notices.
- Extension discovery, trust, load isolation, settings and unload mechanics; remote extensions keep reporting manifest commands as `declared, not provided`.
- The session schema and the event stream; print mode and ACP behaviour.
- The registry's first-source-wins rule. A shadowed command stays shadowed; this change only makes the shadow legible.

## Relationship to the shipped change

`2026-10-23-command-source` is archived and its delta is already folded into `openspec/specs/extensions/spec.md` (requirements *Command registry* and *Extension commands*). This change is a follow-on delta on the same capability: it reproduces both requirements verbatim and appends the new sentences and scenarios, so the two changes read as one seam and the archive history stays linear.

The directory is named `command-source-completion`, distinct from the archived `2026-10-23-command-source/` it follows on from.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `extensions`: the command registry gains source-complete help, command identity and teaching errors; extension commands gain a namespaced id and an invocation lease cancelled by unload; the template command source slot is specified.

## Impact

- `src/Lunate.Coding`: `CommandRegistry` (ids, duplicate diagnostics), `HelpView` (sections), `Commands`/`InteractiveSession.BuiltinCommands` (error-notice wording), `CommandSources` (no signature change).
- `src/Lunate.Extensibility`: `CommandSourceRegistry` gains an in-flight lease and cancel/drain; `ExtensionLoader.Unload` awaits the drain before `context.Unload()`; duplicate diagnostics name ids.
- `src/Lunate.Extensibility.Abstractions`: documentation only. No public API addition, so no `PublicAPI.Unshipped.txt` entry and no API version move — stated explicitly because the shipped change did move it.
- Tests: `Lunate.Coding.Tests` (help listing, registry ids, dispatch notices, extension command end-to-end) and `Lunate.Extensibility.Tests` (unload drain, duplicate ids).
- Docs: `docs/guide.md` (`/help`, extension commands), `docs/spec/extensibility.md` (Commands section).
- No new packages, no session format change, **no new ProjectReference edge** (see design, "Layering").
