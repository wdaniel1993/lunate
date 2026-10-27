# Changelog

Notable changes to Lunate, one section per release. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to use
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are tagged on
[GitHub Releases](https://github.com/wdaniel1993/lunate/releases); the release process itself is in
[`docs/distribution.md`](docs/distribution.md).

## [Unreleased]

Nothing yet.

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

[Unreleased]: https://github.com/wdaniel1993/lunate/commits/main
[0.1.0-rc.1]: https://github.com/wdaniel1993/lunate/compare/v0.1.0-rc.1...HEAD
