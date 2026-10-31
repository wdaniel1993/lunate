## Context

The shipped guard lives in `Lunate.Coding.BashGuard`, wraps the shell in `BashTool`, and resolves/probes its mechanism when the first command runs. `InteractiveSession` prints its welcome header before composing tools; `/context` is rendered by `ContextText`. Settings are resolved through `SettingsStore`. The existing high-risk command detector is `HighRiskCall`; `ToolRiskResolver` only derives a tool's declared risk from annotations. ADR-0024 is accepted and names these four gaps explicitly.

```mermaid
flowchart LR
    Settings[SettingsStore] --> Session[InteractiveSession]
    Session --> Guard[BashGuard and shared status]
    Guard --> Bash[BashTool]
    Bash --> Mechanism[sandbox-exec / bwrap / Landlock / none]
    Guard --> Context[/context and welcome header]
    Session --> Hardlinks[one-time state-root hardlink scan]
    Hardlinks --> Warning[startup warning]
    Command[bash command] --> Risk[HighRiskCall]
    Risk --> Approval[approval policy]
```

This is a lightweight component view of the existing CLI, not a new system boundary. The guard status is resolved once and shared by the shell tool and the two TUI readouts; the risk classifier remains on the existing approval path.

## Goals / Non-Goals

**Goals:**

- Close exactly the four deferred #241 gaps while preserving the guard profile and ordinary shell behavior in ADR-0024.
- Keep the guard enabled by default and make an explicit opt-out observable.
- Keep the new path and risk checks testable without live containers or host state.

**Non-Goals:**

- Turn the guard into a container boundary or claim that it contains a Docker/Podman daemon.
- Change ADR-0024, add a dependency, alter session-file format, or redesign approvals.
- Expand high-risk detection to arbitrary container engines or mount forms not required by the spec.

## Decisions

1. **Extend the current high-risk-call classifier for container mounts.** Parse Docker/Podman `run` mount arguments (`-v`/`--volume` and `--mount type=bind,source=…`) in `HighRiskCall`, resolving the host source against the configured state root using the existing path expansion and canonicalization conventions. A source equal to or beneath the state root is high risk regardless of the container destination or read-only flag: the daemon, not the guarded host command, performs the mount. Keep the check conservative and local to the existing approval classification rather than moving command parsing into `ToolRiskResolver`, which currently handles annotation-derived tool risk only. Tests use temporary host paths assembled with `Path.Combine`, and cover both matches and ordinary mounts.

2. **Scan the pre-existing state root once for externally linked files.** At startup, inspect regular files without following directory symlinks, group paths by filesystem identity, and compare each identity's link count with the number of paths found inside `LUNATE_HOME`. Warn once when at least one link is outside the root, naming an in-root path. Do not create the state root just to scan it, change file contents, or treat two links entirely inside the root as an escape. Route the warning through the existing mode-appropriate startup notice/log channel; ACP protocol output remains stdout-only. Keep filesystem metadata access behind a test seam so fixtures can exercise the finding without reading a real user home.

3. **Make the opt-out a settings-file boolean.** Add top-level `bashGuard` to the existing settings format, defaulting to `true`; `false` disables guard wrapping and probing. Do not add a CLI flag or environment alias in this change. Represent the parsed value internally so the required user setting does not add a public .NET API member. With the guard disabled, BashTool results explicitly say it ran without the guard; no silent opt-out.

4. **Resolve one shared status before the welcome header.** Initialize the guard status during interactive-session startup, before the welcome header is rendered, and pass the same status/guard instance to BashTool, `/context`, and the header renderer. Normalize the displayed mechanism to `sandbox-exec`, `bwrap`, `landlock`, or `none`, and carry the probe result. When opt-out skips probing, report `none` and `not run`; when an attempted probe fails, report `none` plus failure, while preserving the existing detailed one-time warning path. The header uses a short form of the same status. This moves the already-required probe earlier but does not add a second probe.

5. **Keep the verification in existing test seams.** Extend `HighRiskCallTests` for the mount grammar; add deterministic state-root hardlink scan tests; extend `SettingsStoreTests` and BashTool tests for default/disabled behavior; and cover shared status in `/context` and welcome-header tests. Header snapshots may change because the visible header gains status; session persistence fixtures and format do not change. Use host-native paths in all path assertions.

## Risks / Trade-offs

- **[An incomplete mount parser misses an escape]** -> cover the specified Docker/Podman forms and path expansion with positive and negative fixtures; classify ambiguous recognized mount sources conservatively.
- **[An eager mechanism probe adds startup latency]** -> resolve and probe only once per interactive session and reuse the result for BashTool; retain the existing bounded probe timeout.
- **[Filesystem identity APIs differ by platform]** -> isolate identity/link-count lookup behind a seam and test platform-native hardlink fixtures where supported; never infer safety from path spelling alone.
- **[The startup scan may encounter unreadable or changing files]** -> treat scan failures as warnings, continue startup, and do not claim that a partial scan proved the root clean.
- **[A disabled guard weakens persistence protection]** -> default on and state the disabled condition in both the tool result and header.

## Migration Plan

No migration is required. Existing settings omit `bashGuard` and therefore keep the guard enabled. Existing session files remain readable and unchanged. Users who opt out set `bashGuard` to `false`; removing that key restores the default.

## Open Questions

None for this proposal. The required mechanisms, status surfaces, and opt-out behavior are fixed by issue #383; ADR-0024 remains in force.
