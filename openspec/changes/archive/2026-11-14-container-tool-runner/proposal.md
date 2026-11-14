## Why

The `bash` tool runs a model-chosen command on the host, where it can reach everything the user's
account can reach. The S-7 spike (docs/dev/spikes/S-7/report.md) measured that moving only the
process-spawning half into one long-lived Docker or Podman container — with the workspace bind
mounted at the *same absolute path* — keeps the approval panel, the bash guard, verified edits and
the file-change bus working untouched, because the file tools never enter the container. The
maintainer decision on #822 accepted that outcome: process-only execution, the mount as the reach
limit rather than a jail, no guard inside the container, and the state root, install prefix and
`auth.json` never mounted.

#626 landed the seam (`IExecutionEnvironment`, local-only) and the documentation of the intended
shape. This change gives the seam its second implementation: a container environment selected by a
launch flag or the user settings, one container per session, with the cancel hook the spike proved is
required.

## What Changes

- `--env docker:<image>` and `--env podman:<image>` select a container environment for the run; the
  new user-scope settings key `environment` selects the same thing, and `--env` wins over it. A value
  that is not one of the two accepted shapes is refused with the accepted shapes, and a selected
  environment never falls back silently to the host.
- One container per session and workspace, created before the first tool command with
  `run -d --name lunate-<unique> -v <workspace>:<workspace> -w <workspace>` and removed when the
  session ends. The mount is the host's absolute path, never a container-side `/workspace`.
- Each `bash` call runs through `<engine> exec -i -w <workspace> -e LUNATE_SESSION=1 <container>`
  behind the container's `/bin/sh`, so the command sees the same directory as the host tools.
- Cancel and timeout end the command's process group inside the container: the wrapper records the
  container-side process group, the runner sends `TERM`, then `KILL`, and recreates the container
  when the group cannot be proven gone. The host `exec` client is still tree-killed; the spike
  measured that the host-side tree kill alone leaves the container's processes alive.
- The startup header names the selection (`environment podman:alpine`) and `/context` adds its
  readiness (`environment: podman:alpine (ready)`, `starting`, or `failed: <reason>`).
- The direct file tools, ripgrep/grep, approvals, the bash guard, verified edits and the bash
  post-call change scan stay on the host. The image carries nothing from Lunate: the state root, the
  install prefix, `auth.json` and the container engine socket are not mounted or copied.
- `--env` is a usage error with `--acp` in this change: ACP keeps the host path, and a selection must
  never be silently ignored.
- A container end-to-end test runs where it can: Linux with a reachable engine (the Linux CI leg).
  Every other host skips it, and the platform claims stay limited to what ran.
- Docs updated: `docs/execution-environments.md` and `docs/security.md` drop the "pending sign-off"
  wording and state the accepted policy; `docs/cli.md`, `docs/settings.md` and the help text list the
  new flag and key.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `execution-environment`: the seam gains a container implementation selected by flag or setting,
  describes itself with the selection, mounts only the workspace, forwards the session marker, and
  cancels by the container-side process group; local remains the default.

## Impact

- New `src/Lunate.Coding/ContainerExecutionEnvironment.cs` and
  `src/Lunate.Coding/ExecutionEnvironmentSelection.cs`; `LocalExecutionEnvironment` gains an internal
  cancel override so batch execution is shared rather than duplicated.
- `src/Lunate.Coding/LaunchOptions.cs`, `CliHelp.cs` (`--env`), `SettingsStore*.cs` and
  `settings-schema.json` (the `environment` key).
- `src/Lunate.Coding/HarnessFactory.cs` (the environment reaches `BashTool`), `InteractiveSession*`,
  `PrintMode.cs` (selection, header, `/context`, container disposal), `Cli.cs` (`--env` with `--acp`).
- `src/Lunate.Tui/WelcomeHeaderRenderer.cs` and a new `ExecutionEnvironmentStatus` beside
  `BashGuardStatus`.
- Tests: selection parsing, settings resolution, argv mapping and cancel semantics without an engine,
  header/`/context` text, and a Linux-only end-to-end test that is skipped without a reachable engine.
- Docs: `docs/execution-environments.md`, `docs/security.md`, `docs/cli.md`, `docs/settings.md`.
- No new package, no public API change (the settings property is internal), no session-format change.
