## Why

Slash commands are assembled from three parallel places today: the built-in table in `Commands.cs`, the completion registry (which also reads the skill catalog and extension manifests), and the dispatch switch in `InteractiveSession.Commands.cs`. Extensions can declare `commands` in their manifest, and those names appear in `/` autocomplete, but nothing can run them — a declared command is advertised and then fails as unknown.

One command source seam fixes both problems: every command comes from an ordered source, the registry is the single answer for autocomplete, dispatch and `/help`, and extension commands become real only when a source provides them.

## What Changes

- **`ICommandSource`** (public contract, `Lunate.Extensibility.Abstractions`): `SourceId`, `DescribeAsync(CancellationToken)` and `InvokeAsync(CommandInvocation, CancellationToken)`. A descriptor carries the bare name, description, optional argument hint, source id and kind (`Builtin`, `Skill`, `Template`, `Extension`).
- **One registry** (`Lunate.Coding`) merges sources deterministically — built-ins first, then skills, then extensions, then templates — and feeds `/` autocomplete, dispatch and `/help`. First source wins a duplicate name; the conflict is logged.
- **Built-ins become the first source.** The existing table and dispatch switch move behind the seam; user-visible behaviour and `/help` output stay identical.
- **Skills become a source.** `/skill:<name>` keeps today's wrapping, trust checks and notices.
- **Extension manifest `commands` become a real source.** Extensions register a command source through `IExtensionContext.RegisterCommandSource`; a provided and declared command dispatches to the extension. A declared command with no provider is logged and shown in `/extensions` as `declared, not provided`, never listed as runnable.
- **Manifest discipline.** A command provided by code but not declared refuses the load. A collision with a built-in name refuses the load naming both the extension and the built-in.
- **Handler failures never crash the session.** An invocation exception becomes a notice plus a log entry; cancellation is passed through and stays cooperative.
- **API version** moves semver-minor (1.10.0 → 1.11.0) and `PublicAPI.Unshipped.txt` tracks every new public type and member.

## What Does Not Change

- Command parsing, the input history, the busy-session refusals and the notice levels.
- `/skill:<name>` output and the skill index/trust rules.
- Extension discovery, trust, load isolation and unload semantics.
- The session schema, the event stream and print/ACP behaviour.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `extensions`: commands gain a public source contract, one deterministic registry and real extension dispatch; manifest `commands` gain provide/declare rules and `/extensions` reporting.

## Impact

- `src/Lunate.Extensibility.Abstractions`: new command contract and context registration; `ExtensionApi.Current` bump; public API file.
- `src/Lunate.Extensibility`: command source registry, loader cross-checks, reserved-name option, unload cleanup.
- `src/Lunate.Coding`: command registry, built-in/skill/extension sources, dispatch, autocomplete, `/help`, `/extensions`.
- Tests: command registry, completion, extension loading, extension view, active session dispatch.
- Docs: `docs/guide.md` and `docs/spec/extensibility.md`.
- No new packages, no session format change.
