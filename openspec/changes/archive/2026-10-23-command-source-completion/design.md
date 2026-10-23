## Context

The seam exists. `CommandRegistry.ComposeAsync` asks every `ICommandSource` once per composition, sorts by kind rank (`Builtin` 0, `Skill` 1, `Extension` 2, `Template` 3), then source order, then descriptor order, keeps the first entry per name and logs the loser. `InteractiveSession.ComposeCommandsAsync` feeds it `BuiltinCommandSource`, `SkillCommandSource` and the extension wrappers; `DispatchCommandAsync` parses, invokes the owning source and renders at most one message; `CompletionRegistry.Compose` and `HelpView.Lines` both consume `CommandRegistry.Descriptors`.

What the seam does not do is finish the three jobs a registry exists to do: tell a person what is runnable, identify a command unambiguously, and own an invocation's lifetime. `HelpView.Lines` filters `Kind is CommandKind.Builtin`, so help covers one of the four sources. `CommandRegistry` keys `_byName` on the typed token and the duplicate diagnostic names sources, not commands. `ExtensionLoader.Unload` removes the registration and unloads the ALC without consulting any in-flight invocation.

Two constraints frame every decision below.

**ADR-0017, principle 4** (quoted): *"Extensions compile against a small, semver'd contract assembly, never against `Lunate.Agent` internals."* And principle 3: *"Everything crossing the extension boundary is JSON-serializable — no delegates, no live objects; handles instead — so out-of-process extensions in other languages stay possible."* The abstractions cannot see Tui types: no extension-facing signature in this change (or the shipped one) names `KeyEvent`, `CompletionItem` or any other `Lunate.Tui` type — commands cross as `CommandInvocation`/`CommandResult` strings.

**T-49, layering** (quoted from the brief and verified in the tree): *"Lunate.Agent cannot reference Lunate.Extensibility.Abstractions; new ProjectReference edges must be declared in `tests/Lunate.Coding.Tests/LayeringChecker.cs`."* This change adds **no** edge: `Lunate.Coding.csproj` references `Lunate.Extensibility` and not `Lunate.Extensibility.Abstractions`, and the Abstractions types it uses arrive transitively. `LayeringChecker.AllowedReferences` contains no `(Lunate.Coding, Lunate.Extensibility.Abstractions)` pair today, so adding a direct reference later would fail the layering test first — which is the intended tripwire.

## Goals / Non-Goals

**Goals**

- `/help` lists every runnable command the registry knows, with built-in output unchanged and an empty source adding nothing.
- Every command has one stable, source-qualified identity used by diagnostics, logs and error notices.
- An invocation has a defined lifetime across extension unload.
- The template slot has a written contract so P8 lands as a source, not a second seam.
- The public extension surface gets the ADR-0017 addendum it is missing.

**Non-Goals**

- A template command source, template file format, discovery roots or `/help` template content. P8 owns that; this change specifies only the slot.
- MCP prompt commands. The MCP client in `src/` has no prompt support at all — no `prompts/list` call, no `McpPrompt` type — so a prompt source would be a feature change dressed as a registry change.
- Out-of-process command dispatch. Remote extensions keep manifest-only commands reading `declared, not provided`.
- A typed qualified alias (`/ext:<id>:<name>`) as user syntax. See Open Question 4.
- Argument completion for extension commands (`ArgumentHint` stays display-only), a `/help <command>` detail view, or moving the built-in dispatch switch again.
- Any new public API: no `CommandDescriptor` member, no new `CommandKind`, no `ExtensionApi.Current` bump.

## Decisions

### D1. Help reads the whole registry, sections per kind

`HelpView.Lines(IReadOnlyList<CommandDescriptor>)` keeps its signature — it already receives the merged descriptors — and gains sections after the built-in groups:

```
session
  /compact  — …
  …
quit
  /quit — quit
skills
  /skill:review — review the current diff
extensions
  /ping — ping the server
templates
  /standup — draft the standup note
```

Rules: a section is emitted only when it has rows; rows are the typed token plus the description, padded within their own section; sections are ordered by the registry's kind rank (`skills`, `extensions`, `templates`); rows sort ordinal by name. Built-in groups keep their existing order, padding and `Commands.GroupOf` lookup untouched, and the `/skill:<name>` placeholder stays inside the built-in `extensions` group.

The existing `/help` golden (`tests/Lunate.Coding.Tests/InteractiveSessionCommandTests.cs`) asserts the built-in block with `Assert.Contains`, so an appended section cannot break it; a new test pins the empty-source case to today's exact listing. `/skills` keeps its own scope-and-state detail — help lists *names*, `/skills` explains *state*, and the two are allowed to differ.

### D2. Identity is a host-side id, not a new syntax

A new internal helper in `Lunate.Coding` (internal, therefore no PublicAPI line):

```csharp
internal static class CommandId
{
    // builtin:<name> | skill:<name> | template:<name> | ext:<extension id>:<name>
    public static string For(CommandDescriptor descriptor);
    public static string ForSource(string sourceId, string name, CommandKind kind);
}
```

`CommandRegistry` computes the id when it builds a candidate, before the duplicate check, and keeps it on its internal `CommandEntry`. The id is used for:

- the duplicate diagnostic — `command '/ping' from source 'beta' is skipped: 'ext:alpha:ping' already owns it`;
- the handler-failure notice and the log line;
- `/extensions`-adjacent diagnostics where a command is named.

It is *not* used for the typed token, the autocomplete row or the descriptor a source hands in. Sources therefore cannot set or spoof an id, and the extension boundary gains nothing to implement.

`CommandId.For` on a skill descriptor returns its existing `skill:<name>` name — skill ids and skill names coincide, which is why the skill path needs no change.

### D3. The invoke path takes a lease; unload cancels and drains

The wrapper lives in `Lunate.Extensibility`, so `Lunate.Coding` never learns about extension lifecycle:

```csharp
// Lunate.Extensibility/CommandInvocationLease.cs
internal sealed class CommandInvocationLeases
{
    // One gate per extension id. Enter returns a lease that must be disposed.
    internal IDisposable Enter(string extensionId);

    // Cancels every live invocation of the extension, waits for them to leave,
    // and reports whether the drain finished inside `bound`.
    internal ValueTask<bool> CancelAndDrainAsync(
        string extensionId,
        TimeSpan bound,
        CancellationToken cancellationToken);
}

// Lunate.Extensibility/LeasedCommandSource.cs
internal sealed class LeasedCommandSource(
    string extensionId,
    ICommandSource inner,
    CommandInvocationLeases leases
) : ICommandSource;
```

`CommandSourceRegistry.Register` stores the inner source and hands out the `LeasedCommandSource`, so no caller can invoke an extension command without a lease. `LeasedCommandSource.InvokeAsync` enters the lease around `inner.InvokeAsync` and rethrows exactly as before. `CommandSourceRegistry` gains `CancelInFlightAsync(extensionId, bound, ct)`; `ExtensionLoader.Unload` awaits it *before* `context.Unload()`.

Ordering matters: cancel, then drain, then unload the ALC. The runtime keeps a load context alive while its code is on the stack, so the failure mode without this is not a crash — it is a handler that keeps mutating a host whose extension has already been reported gone, and a user who is never told. The drain is bounded by the existing `HookRunnerOptions.HandlerTimeout` (5 s by default — the budget hook handlers already get), so the unload needs no new knob, and a handler that swallows its token cannot hold teardown open; that case is logged once with the command id.

`CommandSourceRegistry.Unregister` keeps its synchronous signature for the three load-failure cleanup sites in `ExtensionLoader.Load.cs`, where no invocation can be in flight; `Unload` uses the async drain instead.

`SetDescriptors` currently matches a registration by object identity (`ReferenceEquals(registration.Source, source)`). With a wrapper in the middle it must match the registration record (`ExtensionId` + inner reference) instead — an implementation note, not a behaviour change, but the kind of thing that silently stamps the wrong descriptors if missed.

### D4. Error notices teach

`CommandRegistry.InvokeAsync` currently returns `CommandResult.Error($"command /{name} failed: {exception.Message}")`. It becomes:

```
command ext:acme:ping failed: connection refused — see /extensions for the extension's state
command skill:review failed: … — see /skills for the skill's state
```

The message names the id (D2), the reason, and one recovery step chosen by kind; a built-in keeps today's shorter form because it has no external source to inspect. No stack trace, no exception type name — the log entry carries the detail. `OperationCanceledException` for a cancelled caller still propagates untouched; cancellation caused by an unload is not the caller's cancellation and surfaces as the D3 notice (`command ext:acme:ping was cancelled because extension 'acme' was unloaded`).

### D5. The template slot is specified by contract, proven by a test source

A `CommandKind.Template` source must compose by the same rules as every other source: order after extension commands, ids of `template:<name>`, appearance under `templates` in help, and no way to shadow a built-in — a template offering `help` loses the duplicate rule and the diagnostic names `builtin:help` and `template:help`. The change ships a fixed test source in `Lunate.Coding.Tests` and no production template code. Nothing in the requirement constrains where templates come from, so P8 can choose files, config or an extension-provided source without reopening the seam.

### D6. ADR-0017 addendum

The command surface is a public extension API, and every extension surface of comparable size in this repo has an addendum (`0017-addendum-model-registry.md`, `-remote-host.md`, `-skills.md`). The text is in this change's `adr.md`; it records the contract-only assembly rule, the primitives-not-Tui rule, registration-only, ordering, identity, failure containment and the in-proc-now / wire-later boundary. It does not create a durable ADR for the registry itself — the registry is core policy, not a contract.

## Risks / Trade-offs

- **Help grows.** A workspace with fifty skills turns `/help` into a wall. Mitigation: sections are per kind and only names — `/skills` remains the detailed view. If it proves noisy, the cap belongs in a follow-on with a real complaint behind it, not a guess now.
- **Ids in user-facing notices.** `ext:acme:ping` is precise and slightly ugly. The alternative — a bare `/ping failed` that cannot say whose — is worse in exactly the case the notice exists for.
- **The drain bound is inherited, not chosen.** Reusing the 5 s hook-handler budget means an unload can wait up to five seconds for a stuck handler. The alternative — a second knob — is worse for one rare path; if the number bites, it moves in one place.
- **Tests can pass without the lease.** A test that unloads an idle extension proves nothing. The red test must start an invocation that awaits, then unload, then assert both the notice and the drain.
- **Reusing the archived change's name.** Cosmetic; see proposal, "Relationship to the shipped change".

## Migration Plan

No data or session migration. Built-in and skill output is unchanged; `/help` gains sections only when non-built-in commands exist; ids appear only in diagnostics and failure notices. No extension source changes shape, so an extension built against 1.11.0 keeps loading and dispatching.

## Sign-off (2026-10-23)

The maintainer approved D1–D5 and D6's addendum with these additions. They are binding on the
implementation, not optional polish.

**Q1 — handler boundary.** In-proc `ICommandSource` with JSON DTOs approved; the addendum names the
ADR-0017 principle-3 exception explicitly. The remote host has shipped (T-52), so a **follow-on card**
now covers a `command/invoke` request over the existing JSON-RPC wire, reusing `CommandInvocation` /
`CommandResult`; remote manifest commands then stop reading "declared, not provided". Not part of this
change, and no wire type is introduced here.

**Q2 — unload mid-command.** Cancel, notice, bounded drain (5 s, inherited from `HookRunnerOptions`),
then unload — plus three rules:

- After cancellation the handler's late result is **discarded**; it is never rendered.
- When the drain ends or times out, the extension's context is marked **disposed**. A later host call
  from that handler throws `ObjectDisposedException`, which is contained and logged **with the command
  id** — never surfaced as a crash or a bare stack trace.
- On session quit the cancellation is **silent**: no notice after the TUI is torn down.

**Q3 — help covers every source by default**, with one cap: the **skills** section shows at most **20**
rows, then `+N more — see /skills` (the skills index already carries this pattern). No cap for
extensions or templates. Help lists **live** entries only: a shadowed command appears in `/extensions`
as `shadowed by <id>`, never in help.

**Q4 — one flat typed namespace**, no typed escape hatch — plus two notes:

- Extension ids must not contain `:`, otherwise `ext:<id>:<name>` is ambiguous. Manifest validation
  refuses a colon, with a test.
- Addendum **decision 5**: the rank of templates relative to extension commands is **provisional**
  until parity item 8 (prompt templates) designs it — a user-authored template probably should beat a
  packaged extension command of the same name. Templates never beat built-ins, now or then.

## Decisions at sign-off (previously open)

Four decisions for the maintainer; the recommendations below are proposed, not settled. They are repeated as `SIGN-OFF` items to be folded into this file when answered.

1. **How an extension handler is invoked.** Recommended: keep the in-proc `ICommandSource` interface with JSON-serializable DTOs (`CommandInvocation` in, `CommandResult` out), and let the remote host add a wire adapter later. Alternative: route every extension command through an event round-trip now. The interface is a live object, which is a real (if narrow) exception to ADR-0017 principle 3 — the addendum should say so explicitly rather than let a reader discover it.
2. **Unload while a command runs.** Recommended: cancel, notice, bounded drain, then unload (D3). Alternatives: wait unbounded for the handler (a stuck handler hangs `/extensions` teardown); refuse to unload while an invocation runs (turns a rare race into a user-visible refusal).
3. **Does help list non-built-ins by default.** Recommended: yes, sections per kind, empty sections omitted. Alternative: `/help` stays built-in-only and `/help all` adds the rest, which keeps scrollback short but leaves discovery to a command nobody knows to type.
4. **Do templates and extension commands share one typed namespace.** Recommended: yes, one flat typed namespace ordered by kind (today's rule), with ids carrying the qualification and no typed escape hatch. Alternative: expose `/ext:<id>:<name>` and `/template:<name>` as typed forms, which makes a shadowed command reachable but adds a second syntax to parse, complete and document.
