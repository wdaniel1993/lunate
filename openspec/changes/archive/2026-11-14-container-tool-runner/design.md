## Context

#626 landed one seam and one implementation:

```
InteractiveSession / PrintMode / subagent workspace factory
        │  HarnessFactory.CreateTools / CreateWorkspaceTools
        ▼
BashTool ──IExecutionEnvironment──▶ LocalExecutionEnvironment ──▶ ChildProcess (posix_spawn)
                                        │  Map: unchanged
                                        │  CancelAsync: children-first tree kill
                                        ▼
                                 BashGuard (host wrapper) · host change scan · verified edits
```

`ProcessRunner` delegates the extension/MCP path to the same seam. The seam already carries the
four values the S-7 spike proved sufficient (executable, argument prefix, working directory,
environment), a `Description`, batch and interactive execution, and a per-process cancel hook.

S-7 measured what a container implementation must do and must not do: mount at the workspace's
absolute host path, one long-lived container per session (create 146 ms, stop+start 10.3 s), forward
`LUNATE_SESSION` with `-e` because `exec` inherits nothing, and kill the container-side process group
because the host tree kill stops only the `exec` client. The maintainer decision on #822 accepted the
process-only policy: the approval panel and the host bash guard stay the controls, the mount is the
reach limit and not a jail, no guard runs inside the container, and the state root, install prefix,
`auth.json` and the engine socket are never mounted.

## Goals / Non-Goals

**Goals:**

- A second `IExecutionEnvironment` implementation that runs `bash` commands in one container per
  session, with the workspace shared at one absolute path.
- Cancellation that provably ends the command inside the container.
- A visible selection: flag, settings key, header line and `/context` readiness, never a silent
  fallback.
- Nothing from Lunate's own state reachable from inside the container.
- An end-to-end proof that runs on the Linux CI leg and skips visibly elsewhere.

**Non-Goals:**

- No container for `read`, `write`, `edit`, `ls`, `glob`, `grep` or ripgrep: the mount makes them
  correct untouched, and the decision keeps them on the host.
- No guard mechanism inside the container, no image build or publish, no image content.
- No ACP container path; no Windows/WSL2 support (a follow-up).
- No per-call container, no stop/start per call, no engine-socket mount, no path translation table.
- No public API change and no session-format change.

## Decisions

### `ContainerExecutionEnvironment` maps to the host engine client and reuses local spawn

`Map` produces the *host half*: the engine executable (`docker` or `podman`), the `exec` argument
prefix, the workspace as the working directory, and `-e` pairs from the request's explicit
environment. `RunAsync` then runs that mapped request through `LocalExecutionEnvironment`, with one
internal extension: a cancel override. The local implementation owns output caps, timeouts, the
line pump and the host tree kill; the container implementation only adds "and then kill the group
inside the container" at the one point a cancel happens. Duplicating the batch runner would give two
places to keep timeout and output-cap behaviour in sync.

```
InteractiveSession / PrintMode / subagent workspace factory
        │
        ▼
BashTool ──▶ ContainerExecutionEnvironment ──Map──▶ <engine> exec -i -w <ws> -e LUNATE_SESSION=1 …
                        │                                   │
                        │  RunAsync (host half)             │
                        ▼                                   ▼
                 LocalExecutionEnvironment ──▶ ChildProcess (the engine client, host side)
                        │  CancelAsync override
                        ▼
             <engine> exec <container> sh -c 'kill -TERM -- -<pgid>; … -KILL'
```

The engine client and the kill/start/remove control calls are host support: they start through the
local environment, never through the tool-command seam, so the process-start inventory keeps its
classification (no new direct `Process.Start` site).

### The workspace is mounted at the host's absolute path

`-v <workspace>:<workspace> -w <workspace>`. This is what makes the file tools, verified edits and
the bash post-call scan correct with no container awareness, and it leaves no path-translation
table to maintain. The container's own filesystem (`/etc`, `/usr`, an image-only toolchain) stays
invisible to the file tools, which is the same boundary they have today.

### One container per session, created lazily before the first command

The container is created on the first `RunAsync` (146 ms) and kept until the session disposes the
environment; stop+start is 10.3 s and would dominate every call. The header can still name the
selection before the container exists because the name comes from the selection, not the engine.
Readiness is tracked from creation: `starting` until the first command, `ready` after the container
runs, `failed: <reason>` when creation failed.

### Cancel records the container-side process group before `exec`ing the command

The command runs behind the container's `/bin/sh`:

```
sh -c 'pgid=$(ps -o pgid= -p $$ | tr -d " "); [ -n "$pgid" ] || pgid=$(cut -d" " -f5 /proc/$$/stat); printf "%s" "$pgid" > /tmp/lunate-<call>.pgid; exec "$@"' lunate-exec /bin/sh -c <command>
```

`exec "$@"` keeps the pid and process group, so the recorded group is the command's. A per-call file
is used instead of a first output line because it does not touch the command's streams: no output
line is filtered, no byte of stdout changes, and a command that writes binary output cannot break
the capture. Cancel sends `TERM`, polls the group for up to about a second, sends `KILL`, and
reports exit codes 3 (no group recorded) or 4 (still alive) for "cannot prove it gone". On 3 or 4
the container is removed and recreated, which is the S-7 answer to "the host tree kill leaves
`sleep 4271` alive". A recorded group of 0 or 1 is treated as unprovable rather than killed.

### The session marker and declared variables are the only environment forwarded

`exec` does not forward the host environment, so the mapping turns the request's explicit
environment (`Environment` plus `ExtraEnvironment`, where `BashTool` puts
`LUNATE_SESSION=1`) into `-e NAME=value` pairs. The engine client itself inherits the host
environment normally, because it needs `PATH`, `HOME`, `XDG_RUNTIME_DIR`, `DOCKER_HOST` and
`CONTAINER_HOST`; the container never sees it. The image supplies everything else.

### The selection lives in the launch options and an internal settings property

`--env` parses in `LaunchOptions` (one flag, two accepted shapes); `SettingsStore` validates the same
shape for the `environment` key and `ApplyLaunch` gives the flag precedence. The resolved value is
an internal property on `AgentSettings`, so no public type or member is added. `--acp` refuses
`--env` rather than ignoring it, because a selection that is silently dropped is exactly the silent
fallback this change forbids.

### Container execution is wired at tool composition, not through `ProcessRunner`

`ProcessRunner` (extension/MCP hosts, restore/Roslyn) stays on local execution in this change: those
are trusted host support services, not model-chosen tool commands, and the card's decision keeps
only process-backed tool commands in the container. The environment reaches `BashTool` through
`HarnessFactory.CreateTools`/`CreateWorkspaceTools` (including subagent workspaces), and the session
owns its disposal.

## Risks / Trade-offs

- **~100–130 ms per bash call** on macOS (S-7: 163.5 → 289.8 ms median including the change scan).
  Accepted: the alternative (per-call start) is 10 s.
- **A missed cancel can leave a process alive.** Mitigated by the recorded group, `TERM`+`KILL`, the
  "cannot prove it gone" exit codes, and recreating the container. The host tree kill still runs.
- **The recorded pgid can be wrong** if the wrapper cannot read `ps` or `/proc`; a recorded `0`/`1`
  or an empty value is unprovable, so the container is recreated instead of killing a group blindly.
- **The image is the user's.** Lunate does not build or publish one, so the toolchain, `sh` and
  `ps`/`/proc` must exist in the image; a missing `sh` surfaces as an engine error naming the image.
- **Platform claims stay narrow.** Only Linux CI runs the end-to-end test; macOS is supported with
  the same mount path but its CI leg skips the test (podman machine is not guaranteed), and Windows
  is a follow-up. The docs say exactly that.
- **The engine socket is reachable by the engine, not mounted by Lunate.** A command inside the
  container can only reach what the image and mount expose; the socket is a host-side path the
  container never sees, which the test asserts.

## Migration Plan

No data migration. The default stays local, and the settings key is new and optional. Removing the
flag/key returns the session to the previous behaviour; a stale container from a crashed session is
removed by name at the start of the next selection in the same workspace only if the name matches
(the name is unique per session, so a `rm -f` before `run` is best effort, and the session removes
its own container on every exit path).

## Open Questions

None requiring a maintainer decision. The security boundary was decided on #822 (process-only, mount
as reach limit, no in-container guard), and the platform matrix from S-7 is carried into the spec and
the docs. WSL2/Windows is named as a follow-up, not decided here.
