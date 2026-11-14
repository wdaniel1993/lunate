# Tasks — container-tool-runner

## 1. Prove the selection and settings shape (test first)

- [x] 1.1 Add failing tests for the selection value: `docker:<image>` and `podman:<image>` parse,
      an image may contain `:` and `/`, anything else is refused with both accepted shapes, and the
      description is the selection value.
- [x] 1.2 Add failing tests for `--env` in `LaunchOptions` (value, `--env=value`, missing value) and
      for the settings key `environment`: it resolves, an invalid value fails the file, a project
      file naming it is refused, and `--env` wins over the settings value.

## 2. The container environment, without an engine (test first)

- [x] 2.1 Add failing tests for the mapped argv: engine executable, `exec -i -w <workspace>`,
      `-e LUNATE_SESSION=1`, the container name, the container-side `sh -c` wrapper, and the command
      unchanged inside it.
- [x] 2.2 Add failing tests for the cancel semantics with a fake host half: a timeout runs the
      container `TERM`/`KILL` script for the recorded group, exit codes 3 and 4 recreate the
      container, a recorded `0`/`1`/empty group is never killed, and the host client is still
      cancelled.
- [x] 2.3 Add failing tests for the lifecycle: one container for two commands, a creation failure
      surfaces as a model-readable refusal and never runs on the host, and disposal removes the
      container.
- [x] 2.4 Add failing tests for the header and `/context`: the header names the selection and gains
      no line for local; `/context` reports `starting`, `ready` and `failed: <reason>`.

## 3. Implementation

- [x] 3.1 `ExecutionEnvironmentSelection` with the two accepted shapes, its parse error and its
      description; `--env` in `LaunchOptions`; the `environment` settings key in
      `SettingsStore`/`settings-schema.json`; `ApplyLaunch` precedence; `--env` refused with `--acp`.
- [x] 3.2 `ContainerExecutionEnvironment`: lazy single-flight container creation, the mapping above,
      the batch path through the local environment with a cancel override, the pgid file, the
      `TERM`/`KILL` script, recreation on an unprovable group, disposal, and the readiness state.
- [x] 3.3 Wire the environment into `HarnessFactory.CreateTools`/`CreateWorkspaceTools`,
      `InteractiveSession` (creation, header, `/context`, disposal) and `PrintMode` (creation,
      disposal); keep ACP, the extension/MCP `ProcessRunner`, ripgrep and the file tools local.
- [x] 3.4 `ExecutionEnvironmentStatus` in `Lunate.Tui` and the header/`/context` rendering.

## 4. End-to-end proof

- [x] 4.1 A Linux-only, engine-guarded end-to-end test: a real `bash` command runs in the container
      at the host's absolute workspace path, a container-written file is visible on the host, the
      session marker arrives, the state root and `auth.json` are absent, and a timed-out command
      leaves no container-side process behind. Skip with a reason elsewhere.
- [x] 4.2 Confirm the exact skip conditions keep the local path and the other CI legs unaffected.

## 5. Docs and reference

- [x] 5.1 `docs/execution-environments.md`: drop the "pending sign-off" status, state the accepted
      policy, correct the lifecycle/name details to what the implementation does.
- [x] 5.2 `docs/security.md`: drop the "pending sign-off" wording and state that the approval panel
      and the host bash guard remain the controls, with the mount as the reach limit.
- [x] 5.3 `docs/cli.md` and the `--help` text: the `--env` flag; `scripts/docs-sync` for
      `docs/settings.md`; the changelog fragment under `changes/<pr>.md`.

## 6. Verify and finish

- [x] 6.1 Focused test classes green: the new selection, environment, cancel and header classes,
      `ExecutionEnvironmentTests`, `ExecutionEnvironmentInventoryTests`, `SettingsSchemaTests`.
- [x] 6.2 `dotnet csharpier format .` then `check`, a Release build of the touched projects,
      `openspec validate --all --strict`.
- [x] 6.3 Archive with `openspec archive container-tool-runner --yes` as the last commit.
