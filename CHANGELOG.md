# Changelog

Notable changes to Lunate, one section per release. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to use
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are tagged on
[GitHub Releases](https://github.com/wdaniel1993/lunate/releases); the release process itself is in
[`docs/distribution.md`](docs/distribution.md).

## [Unreleased]

### Fixed

- The `lunate` tool package shipped its nuget.org page with no licence and no rendered readme:
  `0.1.0` packed with "License missing" and "Readme missing" while the extension package carried
  both. The tool's project now declares the MIT expression, packs the repository README at the
  package root, and names its project and git repository, and
  `scripts/check-package-metadata.sh` — run by the verify gate on both shipped packages and by the
  pack job before the push — opens the packed `.nupkg` and fails when it loses one of the three.
- The slow-hook warning accused an extension of time the machine spent. A dispatch was timed with a
  wall clock around the handler, so a loaded thread pool — where a queued continuation waits for a
  worker — inflated the number: a hook whose own work is nothing reported 648–1392 ms past a 20 ms
  threshold. A crossing is now only called slow when the pool is not the reason it crossed: while
  more work waits for the pool than it has workers, the dispatch is counted but never feeds the
  maximum, the slow count, the warning or the warn-once latch, so a loaded machine cannot name an
  extension for the scheduler's delay and the first crossing on an idle machine still warns.
- A code-mode run that produced nothing at all was reported as "The script timed out", which hid
  the cause. A run that never reached the `hello` handshake now says so, naming the runtime, the
  elapsed time and the missing handshake, and every failure path (timeout, runtime that died, failed
  script) reports the child's exit code and stderr. `StructuredContent` carries `helloSeen` and
  `elapsedMs`, so a loaded runner's slow spawn is visible instead of masquerading as a slow script.
- Three `CodeModeExtension.Tests` cases failed together on a loaded Windows runner: the `node`
  child was started and stayed silent past the 30 s default deadline, so the invitation, the bridged
  ask and the pid never arrived. The tests that are not about the deadline now run with a ceiling
  (120 s) rather than the product default, the kill test yields once so its pid frame is flushed
  before the script blocks the event loop, and an empty pid or ask fails with the run's own report
  instead of a bare assertion.
- The release smoke waits for nuget.org to index the tag's extension package before installing it —
  polling the flatcontainer index, the package content and the registration resource under one
  ten-minute budget — instead of retrying the install five times into the lag. `v0.1.0`'s smoke went
  red on all three operating systems because the package had been pushed but not yet indexed; an
  exhausted budget now fails naming the version and the state of each feed resource, which tells a
  missing package apart from a slow feed.

## [0.1.0] - 2026-10-27

The first stable release. It is `0.1.0-rc.2` plus one fix found on a loaded runner: `ProcessRunner`
abandoned a child's stdout when a write to its stdin hit a closed pipe, so a child that exited while
an answer was in flight looked like one that never answered.

A release candidate is not `latest`, so both installers resolve this build when no version is given.
The `lunate` tool and `Lunate.Extensions.Subagents` publish from the same tag.

## [0.1.0-rc.2] - 2026-10-27

### Added

- The subagents extension ships beside the tool as `Lunate.Extensions.Subagents` — an officially
  supported extension, not a sample. `lunate install Lunate.Extensions.Subagents` installs it from
  NuGet, `lunate packages` lists it with its entry point and API version, `lunate packages --verify`
  recomputes the install record, and `lunate uninstall` removes it. The release smoke proves that
  path from the published feed on all three operating systems.

### Fixed

- A hyphenated tag is published as a prerelease and kept out of `latest`. `v0.1.0-rc.1` published
  as a full release, so `latest/download` — what both installers use when no version is given —
  served a candidate.
- `A_space_after_the_command_closes_the_list` read the input line and the autocomplete list between
  the two writes of one handler, and failed on a loaded runner. The test now waits for the list to
  follow the edit.

## [0.1.0-rc.1] - 2026-10-27

The first release candidate. It exists so the install channels and the recorded media can be
verified against a real, versioned build; the first stable release is `0.1.0`.

### Added

- A native terminal coding agent in one self-contained binary, with an interactive session,
  a live area that keeps the transcript while tools run, prompt editing in `$EDITOR`, and
  session resume and fork.
- C# depth without a language server: `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`,
  `cs_outline` and `cs_rename` from Roslyn, offered only in a C# workspace.
- Extensions in C#: whole packages from NuGet, or a single `.cs` file that declares its tools and
  dependencies, is approved once, compiled and loaded into the running session. Tools, commands,
  hooks, TUI renderers and widgets, MCP servers, model providers and skills all come from the
  same public API, and `Lunate.Roslyn` is the flagship extension built only on it.
- Open protocols: an MCP client, an ACP server for editors, `AGENTS.md` project rules and
  `SKILL.md` skills.
- A trust boundary with a visible approval panel: every write and command asks, edits show their
  diff, a repository asks once, and calls that would extend their own trust (or write the state
  root) always ask. `--yolo` turns the panel off and says so.
- `--print` and `--json` for scripting a run, with a priced footer when the provider reports cost.

### Security

- The approval panel is the boundary: `--yolo` removes it, and a process running with the user's
  shell can write the state root. There is no sandbox in this release; 0.2 adds a bash guard
  profile for macOS and Linux.

[0.1.0]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.2...v0.1.0
[0.1.0-rc.2]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.1...v0.1.0-rc.2
[0.1.0-rc.1]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.1...v0.1.0-rc.2
