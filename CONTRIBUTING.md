# Contributing

Thanks for looking. Lunate is a small, MIT-licensed project and it is built change by change with
coding agents, from the specification up — the rules in [`AGENTS.md`](AGENTS.md) are the ones this
repository actually runs on, and this page is the path from a fresh clone to a merged change.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — **exactly** the version pinned in
  `global.json` (`10.0.402`, `rollForward: disable`, `allowPrerelease: false`). Install it with
  `dotnet-install.sh --version 10.0.402` (`dotnet-install.ps1 -Version 10.0.402` on Windows).
  A newer patch is not picked up automatically: Dependabot opens the bump as a PR against
  `global.json`, and that PR runs the full gate.
- [Node.js](https://nodejs.org/) — runs the documentation lint through a pinned `npx`
- [hyperfine](https://github.com/sharkdp/hyperfine) — measures the startup budget
- `jq` — reads the performance and memory samples (`scripts/perf.sh`, `scripts/memory.sh`)
- [GitHub CLI](https://cli.github.com/) — only for the release runbook in [`docs/distribution.md`](docs/distribution.md)

## Quick start

```bash
git clone https://github.com/wdaniel1993/lunate
cd lunate
dotnet tool restore                       # CSharpier, pinned as a local tool
dotnet build lunate.sln -c Release
dotnet test --solution lunate.sln -c Release
bash scripts/verify.sh                    # the full gate (Windows: scripts/verify.ps1)
```

`scripts/verify.sh` is the done-gate: it restores the tools, builds, runs every test (including a
`de-AT` culture pass and the performance-trait tests), publishes the single-file binary for this
platform, measures the startup budget, exercises the install scripts against a fixture release,
packs and reinstalls the `dotnet tool`, checks the packaging files, formats (CSharpier plus
`dotnet format style` / `dotnet format analyzers`), lints the Markdown and diffs the public API. A
change is not done until it is green — on the CI matrix, all three operating systems count.

A docs-only or proposal-only pull request runs `bash scripts/verify.sh --profile docs` instead:
markdownlint, `openspec validate --all --strict`, `scripts/docs-sync --check` and
`scripts/no-docs-references.sh` (which fails if anything under `docs/` becomes a build input or a
test's subject). `scripts/ci-plan.sh` picks the profile from the PR's changed paths, deny-by-default:
only `docs/`, `adr/`, `openspec/` and root-level `*.md` qualify, and a change to the workflows, a
rename out of those trees or anything else means the full gate. The default `--profile full` is the
whole gate above, unchanged.

Useful narrower commands while working:

```bash
dotnet test --project tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj -c Release
dotnet csharpier format .                # never hand-format C#
npx --yes markdownlint-cli2@0.23.3       # documentation lint only
bash scripts/verify.sh --profile docs    # the prose gate: lint, OpenSpec, generated regions
bash scripts/perf.sh artifacts/publish/<rid>/lunate   # startup budget only
bash scripts/gate-tests.sh               # the gate's own self-tests (needs a published binary)
```

## Repository map

| Path | What lives there |
| --- | --- |
| `src/Lunate.Ai/` | `IChatClient` setup, model catalog, record and replay clients |
| `src/Lunate.Agent/` | the loop on `IChatClient`, events, tools, sessions, compaction |
| `src/Lunate.Extensibility/` | the extension host: discovery, loading, hooks, trust |
| `src/Lunate.Extensibility.Abstractions/` | the semver'd contract extensions compile against |
| `src/Lunate.Extensibility.Remote/` | the out-of-process extension host and its wire contracts |
| `src/Lunate.Extensibility.Testing/` | the test kit that loads a built extension through the real loader |
| `src/Lunate.Protocols/` | MCP client and ACP server |
| `src/Lunate.Tui/` | Spectre output, input line and live area |
| `src/Lunate.Roslyn/` | built-in semantic C# extension, loaded on first use |
| `src/Lunate.Coding/` | the `lunate` executable: TUI, print and ACP modes |
| `tests/` | one test project per src project, plus fixtures |
| `openspec/specs/` | current behaviour, the source of truth |
| `openspec/changes/` | in-flight proposals and tasks |
| `adr/` | durable architectural decisions |
| `docs/` | guide, spikes, eval write-ups and the documentation map |
| `templates/`, `samples/` | the extension template and the reference extensions |

## Documentation

Start at [`docs/README.md`](docs/README.md) — the map of every document kind and the writing
guideline. Key entry points:

- [Guide](docs/guide.md) — what Lunate is, the plan and the roadmap
- [Architecture](docs/architecture.md) — the component map and the runtime flows
- [Specs](openspec/specs/) — the behaviour source of truth
- [ADRs](adr/) — decisions with their context
- [Releases](https://github.com/wdaniel1993/lunate/releases)

One home per fact: link to the document that owns a fact instead of copying it, and update the
documents a change invalidates.

## The OpenSpec workflow

Work happens through OpenSpec changes, and the artifacts are the source of truth:

1. **Propose** — describe the change; the proposal, the delta specs and the tasks land on `main`
   before any implementation.
2. **Apply** — implement the tasks on a branch from `main`, test-first, ticking tasks as they land.
3. **Verify** — check the implementation against the change's artifacts before archiving
   (`openspec validate <change> --type change --strict`).
4. **Archive** — sync the delta specs into `openspec/specs/` and archive the change as the *final
   commit of the implementation pull request*, added after review and before merge, so one reviewer
   sees the work and the bookkeeping that closes it.

A pull request that only archives a change is refused by the `archive-only` check. A standalone
archive is still legitimate when a change merged before its archive existed: label the pull request
`standalone-archive` and say why in the body.

One change per session. Do only what the change's `tasks.md` asks; anything else becomes a follow-up
change. `openspec/specs/` is the source of truth for behaviour — if code and spec disagree, stop and
fix the spec first. Architectural decisions (the loop, tool contracts, protocols, dependencies)
require maintainer sign-off: propose and stop.

## Pull requests

Pull requests use the [pull request template](.github/pull_request_template.md): what the change is,
what lands, where to look first, and the verification you ran. Keep commits small and conventional
(`feat`, `fix`, `docs`, `test`, `chore`). Public API changes belong in
`PublicAPI.Unshipped.txt`, no new NuGet package arrives without an ADR, and a failing golden session
test means the session format changed: stop and report instead of editing the golden file.

## Trust in scripted runs

A repository's project extensions and its project `.lunate/mcp.json` run repository-provided code, so
each needs a one-time trust decision. Records live in `~/.lunate/trust.json` — per user and machine —
so an ephemeral CI runner starts untrusted and runs the trust step in every job:

```bash
lunate trust            # inspect: the records, the pending decisions and one digest
lunate trust --yes      # record the pending decisions
lunate trust --json     # the inspection as one JSON document, for scripts
lunate trust --clear    # remove this repository's records
```

`lunate trust` exits 3 when decisions are pending, so it doubles as a CI check. `--yes` records
exactly what it prints: use it only for checkouts you control, never on untrusted pull requests or
forks. To review before trusting in CI, inspect `lunate trust --json` on the reviewed revision, pin
the digest it prints, and let the job run `lunate trust --yes --digest <sha>`: it records only when
the recomputed digest matches, and a stale digest records nothing and exits 4.

## Security

Do not report vulnerabilities in a public issue — see [`SECURITY.md`](SECURITY.md).

License: MIT — see [`LICENSE`](LICENSE).
