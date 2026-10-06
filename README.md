# Lunate

A native C# coding agent for the terminal — a reliable core loop, four core tools (`read`, `write`, `edit`, `bash`), a Spectre-based TUI, and first-class C# support through Roslyn.

Named for the **lunate sigma (Ϲ)**: the ancient crescent-shaped sigma that reads like a Latin "C" — a Greek-letter sibling of π and τ that points directly at C#.

Status: **bootstrap** — the core is built change by change; providers and the agent-event contract have landed, there is no release yet. The plan and roadmap live in [`docs/guide.md`](docs/guide.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the version is pinned in `global.json`)
- [Node.js](https://nodejs.org/) — runs the documentation lint in the verify gate (pinned `npx`)
- [hyperfine](https://github.com/sharkdp/hyperfine) — measures the startup budget
- `jq` — only for `scripts/memory.sh`

## Quick start

```bash
dotnet tool restore                     # CSharpier, pinned as a local tool
dotnet build lunate.sln -c Release
dotnet test --solution lunate.sln -c Release
bash scripts/verify.sh                  # the full gate (Windows: scripts/verify.ps1)
```

`scripts/verify.sh` runs the build, all tests (including a `de-AT` culture pass), a single-file publish, the startup budget, formatting (CSharpier), the documentation lint and the public-API check.

## Repository map

| Path | What lives there |
| --- | --- |
| `src/Lunate.Ai/` | `IChatClient` setup, model catalog, record and replay clients |
| `src/Lunate.Agent/` | the loop on `IChatClient`, events, tools, sessions, compaction |
| `src/Lunate.Protocols/` | MCP client and ACP server |
| `src/Lunate.Tui/` | Spectre output, input line and live area |
| `src/Lunate.Roslyn/` | built-in semantic C# extension, loaded on first use |
| `src/Lunate.Coding/` | the `lunate` executable: TUI, print and ACP modes |
| `tests/` | one test project per src project, plus fixtures |
| `openspec/specs/` | current behaviour, the source of truth |
| `openspec/changes/` | in-flight proposals and tasks |
| `adr/` | durable architectural decisions |
| `docs/` | guide, spikes, eval write-ups and the documentation map |

## Documentation

Start at [`docs/README.md`](docs/README.md) — the map of every document kind and the writing guideline. Key entry points:

- [Guide](docs/guide.md) — what Lunate is, the plan and the roadmap
- [Specs](openspec/specs/) — the behaviour source of truth
- [ADRs](adr/) — decisions with their context
- [Releases](https://github.com/wdaniel1993/lunate/releases)

## Contributing

- `scripts/verify.sh` (Windows: `scripts/verify.ps1`) must pass before a change is done.
- Work happens through OpenSpec changes; [`AGENTS.md`](AGENTS.md) has the rules.
- Pull requests and issues use the templates in `.github/`.

License: MIT — see [`LICENSE`](LICENSE).
