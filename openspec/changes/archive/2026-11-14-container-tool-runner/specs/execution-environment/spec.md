## RENAMED Requirements

- FROM: `### Requirement: Local execution is the only implementation`
- TO: `### Requirement: Local execution is the default implementation`

## MODIFIED Requirements

### Requirement: One tool-command execution-environment seam

Lunate SHALL define exactly one internal seam for tool-command processes:
`IExecutionEnvironment`. The seam SHALL expose:

- a one-line `Description` for the header or `/context` line: `local` for local execution, and the
  selected `<engine>:<image>` value for a container environment;
- a mapping from a command's executable, argument prefix, working directory and environment to the
  concrete values the environment runs — with no path-translation table, because a bind-mounted
  workspace keeps one absolute path on both sides;
- a cancel hook for a command it started, so a container implementation ends a container's process
  group instead of a host process tree;
- batch and interactive execution used by the same `IProcessRunner` seam the extension/MCP session
  and restore/Roslyn paths already consume.

`ProcessRunner` SHALL delegate its `RunAsync` and `StartSessionAsync` calls to the environment, so
the extension path cannot bypass the seam.

#### Scenario: A tool command is mapped before it runs

- **WHEN** a tool command reaches the environment
- **THEN** the environment receives its executable, argument prefix, working directory and
  environment through the mapping, and runs the mapped command

#### Scenario: The extension process path uses the same seam

- **GIVEN** an extension or MCP session that consumes `IProcessRunner`
- **WHEN** it starts a batch command or an interactive process
- **THEN** `ProcessRunner` delegates that call to the same execution environment

#### Scenario: The environment describes itself

- **WHEN** the active environment is read for the header or `/context` line
- **THEN** it returns one short description: `local` for local execution, the selection for a
  container environment

#### Scenario: Cancellation is the environment's hook

- **GIVEN** a tool command started by an environment
- **WHEN** the command is cancelled or times out
- **THEN** the environment's cancel hook ends it with that environment's semantics; local execution
  keeps the existing process-tree kill, and a container environment ends the container-side process
  group as well

### Requirement: Local execution is the default implementation

`LocalExecutionEnvironment` SHALL remain the default `IExecutionEnvironment` and SHALL be the only
implementation used when no environment is selected. It SHALL map commands unchanged and SHALL
preserve today's spawn, output cap, timeout, line-pump, environment, exit-code and tree-kill
behaviour. Selecting no environment SHALL NOT add a flag, setting, public API or file-system
abstraction to the local path.

`ContainerExecutionEnvironment` SHALL be the second implementation, selected only by the launch flag
or the user settings described below.

#### Scenario: The local mapping changes nothing

- **GIVEN** a command with an executable, arguments, working directory and environment
- **WHEN** the local environment maps it
- **THEN** the executable, arguments, working directory and environment are unchanged

#### Scenario: Existing process behaviour is unchanged

- **GIVEN** the existing `ProcessRunner` and `ProcessSession` suites
- **WHEN** they run through the local environment
- **THEN** exit codes, streams, output caps, line exchange, environment replacement and tree kills
  behave exactly as before

#### Scenario: No selection keeps the local path

- **GIVEN** a session with neither the flag nor the settings key
- **WHEN** a `bash` command runs
- **THEN** it runs through local execution, and no container engine is started

### Requirement: File tools stay host-side and the inventory has no silent bypass

The direct file tools `read`, `write`, `edit`, `ls`, `glob` and `grep`'s file reads SHALL remain on
host filesystem access and SHALL NOT be routed through the execution environment, because they
never spawn a process and the workspace is mounted at the host's absolute path. The ripgrep search
giant SHALL also stay on the host: file reads and the process that performs them are host support in
this change. Every process-start site in `src/` SHALL be classified as either a tool command
(through the seam) or host support (local), and a test SHALL fail when a new site appears without a
classification.

#### Scenario: File tools keep host access

- **GIVEN** a read, write or edit call
- **WHEN** the workspace files are accessed
- **THEN** the tool uses host filesystem access and never asks an execution environment to run a
  process

#### Scenario: An unclassified process start fails the inventory

- **GIVEN** a new direct process start under `src/`
- **WHEN** the inventory test runs
- **THEN** it fails until the site is classified as a tool command or as host support

## ADDED Requirements

### Requirement: A container environment is selected by flag or user setting

Lunate SHALL accept exactly two selection forms — `docker:<image>` and `podman:<image>` — from the
launch flag `--env` and from the user-scope settings key `environment`. Both engines SHALL be
supported and SHALL differ only in the executable the runner starts. `--env` SHALL win over the
settings key for that run. A value that is not one of the two accepted shapes SHALL be refused with
the accepted shapes, in the flag and in the settings file alike, and a selected environment SHALL
NOT fall back silently to the host. `--env` SHALL be a usage error with `--acp`, which keeps host
execution in this change.

#### Scenario: The flag selects a Docker or Podman container

- **GIVEN** a run started with `--env docker:alpine` or `--env podman:alpine`
- **WHEN** the session composes its tools
- **THEN** process-backed tool commands use the container environment named by the selection

#### Scenario: The settings key selects the same environment

- **GIVEN** a user settings file with `"environment": "podman:alpine"`
- **WHEN** a run starts without `--env`
- **THEN** the container environment is selected, and with `--env docker:debian` the flag's
  selection is used instead

#### Scenario: An invalid selection is refused

- **GIVEN** a selection that is neither `docker:<image>` nor `podman:<image>`
- **WHEN** the run starts or the settings file resolves
- **THEN** it is refused with the accepted shapes, and nothing runs on the host in its place

#### Scenario: ACP keeps the host path visibly

- **GIVEN** `--acp` combined with `--env`
- **WHEN** the launch options parse
- **THEN** it is a usage error naming ACP, never a silently ignored selection

### Requirement: One container per session, mounted only at the workspace

Lunate SHALL create one container per session and workspace, before the first tool command, and
SHALL keep it for the session's lifetime because a stop plus start measured about 10.3 s. The
container SHALL be created with the workspace bind-mounted at the host's absolute path and with the
same absolute path as its working directory (`-v <workspace>:<workspace> -w <workspace>`), never at
a fixed container-side path. The session SHALL remove the container when it ends.

The container SHALL receive only the workspace and the explicitly named tool-command environment
variables; the state root, the install prefix, `auth.json` and the container engine socket SHALL NOT
be mounted, copied or otherwise made reachable from inside. Lunate SHALL not build, publish or
otherwise fill an image with agent material: the image is the user's.

#### Scenario: The command runs in the container at the host's path

- **GIVEN** a selected container environment and a workspace
- **WHEN** a `bash` command runs
- **THEN** it runs inside the container, with the workspace's absolute host path as its working
  directory, and a file it writes is immediately visible on the host

#### Scenario: The state root and credentials are absent inside

- **GIVEN** a selected container environment, a state root and an `auth.json`
- **WHEN** a command checks for them inside the container
- **THEN** they are absent, because neither is mounted

#### Scenario: The engine socket is not mounted

- **GIVEN** a selected container environment
- **WHEN** the container is created
- **THEN** the container engine's socket is not among its mounts

### Requirement: Container commands run through exec with the session marker

Each process-backed tool command in a container environment SHALL run through the engine's `exec`
in the session's container, in the workspace, with a POSIX `sh` capturing the container-side process
group before running the command. Because `exec` does not forward the host environment, the session
marker `LUNATE_SESSION` SHALL be passed explicitly with `-e` for every command. The direct file
tools, the approval panel, the host-side bash guard, verified edits and the bash post-call change
scan SHALL be untouched by the container environment; the guard SHALL keep wrapping the host half of
the command.

#### Scenario: A container bash command sees the session marker

- **GIVEN** a selected container environment
- **WHEN** a `bash` command reads `LUNATE_SESSION`
- **THEN** it reads the session marker value, because the runner passed it with `-e`

#### Scenario: The host change scan still publishes a container write

- **GIVEN** a selected container environment in a git workspace
- **WHEN** a container `bash` command changes a tracked file
- **THEN** the host-side post-call scan publishes the change

### Requirement: Cancel ends the container's process group

When a container-backed command is cancelled or times out, Lunate SHALL end that command's process
group inside the container, in addition to tree-killing the host `exec` client. The runner SHALL
capture the container-side group id when the command starts, SHALL send `TERM`, wait briefly, send
`KILL`, and SHALL remove and recreate the container when the group cannot be proven gone. A cancel
SHALL NOT be reported as successful while the command's container-side processes may still run.

#### Scenario: A timed-out command leaves no container process behind

- **GIVEN** a container environment and a command that outlives its timeout
- **WHEN** the command times out
- **THEN** the command's process group is gone inside the container, and the host `exec` client is
  killed too

#### Scenario: An unprovable group recreates the container

- **GIVEN** a cancel whose container-side process group cannot be proven gone
- **WHEN** the cancel finishes
- **THEN** the container is removed and recreated for the next command, so a later command does not
  share state with the cancelled one

### Requirement: The header and /context name the active environment

The startup header SHALL name the active environment on its own line (`environment
<podman|docker>:<image>`) when one is selected, and SHALL NOT add that line for the local default.
`/context` SHALL add the environment and its readiness (`environment: <selection> (ready)`,
`starting`, or `failed: <reason>`). Both lines SHALL be formatted culture-invariantly and SHALL be
built from the environment's own `Description`.

#### Scenario: The header names the selection

- **GIVEN** a session started with `--env podman:alpine`
- **WHEN** the header prints
- **THEN** it contains the environment line naming `podman:alpine`

#### Scenario: /context names readiness

- **GIVEN** a session with a selected environment
- **WHEN** `/context` runs before and after the first command
- **THEN** it reports the environment as `starting` before the container exists and `ready` after it
  started, or `failed: <reason>` when it could not start

### Requirement: Container execution is proven where it runs

The container environment SHALL have an end-to-end test that runs a real `bash` command and a real
cancel against a real engine, and that test SHALL run on the Linux CI leg while it skips on a host
without a reachable engine. Platform support SHALL be claimed only for hosts the tests ran on:
Linux and macOS with the same absolute mount path are the supported hosts, and Windows is a
follow-up (WSL2 with a translated mount), never a promise in this change.

#### Scenario: Linux CI proves the container path

- **GIVEN** a Linux host with a reachable container engine and an image
- **WHEN** the end-to-end test runs
- **THEN** it proves a command ran in the container, the workspace stayed shared, and the cancel
  removed the container-side process group

#### Scenario: A host without an engine skips visibly

- **GIVEN** a host where the engine is absent or the required platform is not Linux
- **WHEN** the end-to-end test runs
- **THEN** it skips with a reason instead of failing or silently passing
