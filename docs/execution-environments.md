# Run tool calls in a container

Lunate runs tool commands on the host by default. A container environment moves the command half
of the agent — `bash` and every other tool that spawns a process — into one long-lived Docker or
Podman container, while the agent loop, approvals and the direct file tools stay on the host. The
workspace is mounted at the same absolute path on both sides, so `read`, `write`, `edit`, `ls`,
`glob` and `grep` keep reading and writing the same files without knowing a container exists.

The container limits what a command can reach. It does not replace the approval panel or the bash
guard; [Run Lunate safely](security.md) compares the layers and [S-7](#evidence-and-limits) records
what was measured.

> **Status.** Container execution is implemented in
> [#627](https://github.com/lunate-agent/lunate/issues/627). This page records the shape, the
> settings and the measured limits. The security policy is decided: process-only execution, the
> approval panel and the host bash guard stay the controls, no guard runs inside the container, and
> the mount is the reach limit rather than a jail.

## One complete flow

1. Have a container engine and an image with a POSIX `sh` (Lunate uses it to capture the container
   side process group). On macOS with Podman, start the machine first: `podman machine start`. On a
   Linux host with Docker, start the daemon. The image supplies the toolchain the command uses; the
   workspace comes from the mount, not from the image.
2. Select the environment for the run:

   ```bash
   lunate --env podman:alpine
   # or
   lunate --env docker:alpine
   ```

   The same selection can live in the user settings file, and `--env` wins over it:

   ```json settings
   {
     "schemaVersion": 2,
     "environment": "podman:alpine"
   }
   ```

3. On the first command call, Lunate creates one container for the session and workspace:

   ```text
   <engine> run -d --name lunate-<session> -v <workspace>:<workspace> -w <workspace> <image>
   ```

   The mount is the canonical workspace at the same absolute path, never `/workspace`. Creation
   takes about 146 ms and is reused for every later call.
4. Each `bash` call runs through the engine's `exec` in that workspace, with the session marker
   forwarded explicitly because `exec` does not inherit the host environment. The container's `/bin/sh`
   records the command's process group, then execs the command unchanged:

   ```text
   <engine> exec -i -w <workspace> -e LUNATE_SESSION=1 <container> /bin/sh -c '<group wrapper>' lunate-exec /bin/sh -c <command>
   ```

5. The host runs its normal post-call change scan over the mounted workspace. A file the container
   command created or changed appears on the host immediately and is published by the existing
   `bash` change path.
6. The session removes the container when it ends. There is no stop-and-start per call, because a
   stop plus start measured about 10.3 s.

The startup header names the selection (`environment podman:alpine`) and `/context` adds its
readiness (`environment: podman:alpine (ready)`, or `starting` / `failed: <reason>`). With no
selection, neither line gains an environment segment.

### Selection and settings

| Selection | Where | Effect |
| --- | --- | --- |
| `--env docker:<image>` | launch flag | Runs process-backed tools in a Docker container. |
| `--env podman:<image>` | launch flag | The same, through Podman. |
| `environment` | user settings (`~/.lunate/settings.json`) | The saved selection; `--env` overrides it for the run. |
| unset | — | Today's host execution, unchanged. |

A value that is not the accepted `docker:<image>` or `podman:<image>` shape is refused with the
accepted shapes; a selected environment never falls back silently to the host. ACP keeps the host
path in this change.

## What runs where

| Work | Where it runs |
| --- | --- |
| `bash` and every process-spawning tool | Container |
| `read`, `write`, `edit`, `ls`, `glob`, `grep` | Host |
| Agent loop and approvals | Host |
| Write verification | Host |
| File-change bus and the `bash` post-call scan | Host |
| Container engine client (`docker` / `podman`) | Host |

The file tools do not enter the container and do not need to. The workspace is mounted at the same
absolute path on both sides, so a host write is the byte-identical file the container reads, and a
container write is the file the host reads back. A container-side spelling such as
`/workspace/file.txt` is not a second path: the host boundary refuses it, because the workspace is
only mounted where the host already has it.

That also means the image's own files — `/etc`, `/usr`, a toolchain installed only in the image —
are not visible to the file tools. The shared surface is the workspace, not the container's whole
filesystem.

## What is not mounted

The container gets the workspace and nothing from Lunate's own state: the state root (`LUNATE_HOME`,
`~/.lunate`), the install prefix and `auth.json` are never mounted. The S-7 run confirmed the state
root is absent from inside (`test -e` returns false), and the container receives only the image's
environment plus the explicit `LUNATE_SESSION=1` marker, not the host's environment.

A command can still reach whatever the engine itself exposes (the engine socket, for example), and
it can reach the workspace. The mount is a reach limit, not a jail.

## The security layers

The approval panel, the bash guard, verified edits, the file-change bus and the container compose.
They answer different questions:

- The **approval panel** is intent: a human says yes to this command, on the host, before any engine
  client starts.
- The **bash guard** is host state: the command's host half cannot write the trust store or the
  install prefix and cannot read `auth.json`; a refusal names the guard and happens before the
  container is touched. The guard wraps the host `exec` client, not the command inside the
  container.
- **Verified edits** are host-side correctness: the file the verifier opens is the file the
  container reads through the mount.
- The **file-change bus** is host-side history: the post-call scan sees a container `bash` write
  through the mount and publishes it as a `bash` change.
- The **container boundary** is reach: the command sees the workspace mount and the image, not the
  state root, the install prefix or the credentials.

The container replaces neither the approval panel nor the guard. The accepted policy is
process-only: the approval panel and the host bash guard remain the controls, the guard wraps the
host `exec` client, and no guard runs inside the container. The mount boundary — and with it the
absent state root, install prefix and credentials — is what limits the inner command's reach. That is
the decision recorded on #627, not a claim that the container is guarded by BashGuard.

## Cost and lifecycle

The S-7 measurements are one macOS host with Podman and a Linux arm64 container. They show the order
of magnitude, not a budget:

| Measurement | Result |
| --- | --- |
| Container create + start | 146.2 ms median (5 samples, 136.3–164.1 ms) |
| Container stop + start | 10.28 s median (3 samples, 10.26–10.34 s) |
| Warm `exec` client + `/bin/true` | 0.201 s median in the earlier five-trial run; 0.138 s median in the later 15-trial run |
| `bash` tool on the host | 163.5 ms median, including the post-call change scan |
| `bash` tool through the container | 289.8 ms median, including the same scan — about 126 ms more (an earlier run measured +97 ms; treat the overhead as 100–130 ms) |

The first two rows drive the lifecycle: one container per session and workspace, created before the
first command and kept until the session ends. The container must already be running; stopping and
starting it per call would dominate every command. The `exec` cost is the engine CLI round trip,
not the command itself, and the file tools are unaffected (the same run measured host `write` at
31 ms and `read` at 5 ms).

Not measured: an image pull, a cold `podman machine`, the Docker engine, a native Linux host, a real
workload, and Windows. Do not turn these numbers into a regression budget until a CI run produces
them on the platform it guards.

## When the engine or machine is not running

A missing engine or a stopped Podman machine is a visible failure, not a silent switch to the host.
[Troubleshooting](troubleshooting.md#the-container-engine-is-not-running) names the message and the
next step; the command does not run on the host in either case.

## Cancel

Cancel and timeout end the command's process group inside the container: the runner captures the
container-side group id, sends `TERM`, waits briefly, sends `KILL`, and removes and recreates the
container if the group cannot be proven gone. The host `exec` client is still tree-killed. The spike
measured the reason this matters: the host-side tree kill alone left `sleep 4271` running in the
container, while killing the reported process group removed it. Cancel therefore ends the command in
both places, not only the host client.

## Platform support

Only what has been measured is claimed. The same absolute-path mount is the intended shape on Linux
and macOS hosts; Windows cannot expose `C:\…` at the same path, so it needs WSL2 with a translated
mount and is a follow-up, not a promise.

| Host | Engine | Container | Status |
| --- | --- | --- | --- |
| macOS | Podman 6.1.0 with an Apple-VM `podman machine` | Linux arm64 | Demonstrated by S-7. |
| Linux | Docker Engine | Linux | Covered by the Linux-only end-to-end test in `Lunate.Coding.Tests`, which runs in the Linux CI leg with Docker. |
| Linux | Podman | Linux | Not demonstrated. |
| Windows | any | — | Not supported in this mode; WSL2 with a translated path is a follow-up. |

The intended support is Linux and macOS with the workspace at the same path on both sides. The
Linux Docker row is proven by the container end-to-end test, which skips with a visible reason on a
host without a reachable engine; the macOS row has S-7's measured evidence. Docker Engine on macOS,
a native Linux host with Podman, and Windows/path translation were not demonstrated separately, so
this page claims none of them.

## Evidence and limits

The evidence is the S-7 spike ([report and raw output in PR #647](https://github.com/lunate-agent/lunate/pull/647),
landing under `docs/dev/spikes/S-7/`). One run: macOS 26.3.1 arm64, Podman 6.1.0, an Apple-VM
`podman machine` with 4 vCPU and 4 GiB, image `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04,
aarch64). It drove the real `BashTool`, `ReadTool` and `WriteTool`, not a reimplementation.

The cost rows use 15 samples except stop/start (3) and create (5). Both `bash` rows include the
tool's own post-call change scan, which is why the local median is 163.5 ms rather than a few
milliseconds; the difference between them is the container overhead. The run did not test an image
pull, a cold machine, Docker, a real workload or Windows, so those remain gaps rather than assumed
behaviour.

## Related

- [Run Lunate safely](security.md) — how approval, the bash guard, verified edits, the file-change
  bus and the container boundary compose.
- [Settings reference](settings.md) — every settings key, generated from the schema.
- [Usage](usage.md) — approvals, tool blocks and the modes.
- [S-7 report](https://github.com/lunate-agent/lunate/pull/647) — the measured evidence and raw
  output, landing under `docs/dev/spikes/S-7/`.
