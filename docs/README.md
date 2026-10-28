# Documentation map

Lunate keeps each kind of document in exactly one home. Start here, then jump to the document that owns the fact you need.

| Document kind | Home | What it owns |
| --- | --- | --- |
| Project entry point | [`../README.md`](../README.md) | What Lunate is, install, quick start, features, configuration and modes |
| Contributor guide | [`../CONTRIBUTING.md`](../CONTRIBUTING.md) | Prerequisites, build, the verify gate, the repository map, the OpenSpec workflow |
| Security policy | [`../SECURITY.md`](../SECURITY.md) | How to report a vulnerability, the trust model, secrets |
| Changelog | [`../CHANGELOG.md`](../CHANGELOG.md) | Notable changes, one section per release |
| Plan and status | [`guide.md`](guide.md) | The implementation plan, phases and roadmap |
| Architecture overview | [`architecture.md`](architecture.md) | Component map, model pipeline, one run and the event path, as diagrams |
| Extensibility spec | [`spec/extensibility.md`](spec/extensibility.md) | Contract and hooks, tool model, services, UI by mode, fitness suite (under ADR-0017) |
| Export contract | [`spec/export.md`](spec/export.md) | `/export` and `/copy`: destination, credential redaction, the HTML document's CSP and escaping guarantees |
| Extension authoring | [`extensions/`](extensions/README.md) | Quickstart, manifest reference, single-file headers, per-surface API and constraints, lifecycle and trust, testing, troubleshooting |
| Installing packages | [`extensions/packages.md`](extensions/packages.md) | The bundle manifest, dependency resolution and its lock, the install record, both scopes, upgrade and uninstall, offline and private feeds, trust after install |
| Remote extensions | [`extensions-remote.md`](extensions-remote.md) | The out-of-process manifest and wire reference for extension authors (schema: [`schemas/extensions-remote.schema.json`](../schemas/extensions-remote.schema.json)) |
| Roslyn MCP server | [`mcp-roslyn.md`](mcp-roslyn.md) | `lunate mcp roslyn`: the C# tools over stdio, and what the server leaves out by design |
| Distribution runbook | [`distribution.md`](distribution.md) | Install scripts, release assets, channel ops and the first-release checklist |
| Behaviour source of truth | [`../openspec/specs/`](../openspec/specs/) | Current behaviour, one folder per capability |
| In-flight proposals | [`../openspec/changes/`](../openspec/changes/) | Proposals, deltas, designs and tasks being implemented |
| Decisions with context | [`../adr/`](../adr/) | Durable architectural decisions, immutable once accepted |
| Evidence | [`spikes/`](spikes/) | Spike code and reports that back an ADR |
| Evaluation and audits | [`eval/`](eval/) | Head-to-head results and surface audits that back a positioning claim |

## Writing guideline

- **English-first.** User- and model-facing text is English; technical output is culture-invariant.
- **Linted.** `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`, `AGENTS.md`,
  `docs/**/*.md` and `adr/**/*.md` are checked by markdownlint
  (`npx --yes markdownlint-cli2@0.23.3`, configured in `.markdownlint-cli2.yaml`). `openspec/` is
  tool-managed and excluded.
- **One home per fact.** Link to the owning document instead of copying it; a fact that exists twice drifts.
- **Generated regions are not hand-written.** `scripts/docs-sync` fills
  `<!-- keys:start --> … <!-- keys:end -->` in `guide.md` from `src/Lunate.Tui/keys.json` (the file
  the router and the in-app help read), and expands `<!-- snippet: <path> -->` into a fenced block
  taken from that real file — the marker owns the block below it. Edit the source, then run
  `scripts/docs-sync`; `--check` is what the gate runs.
- **Behaviour changes update the specs first.** `openspec/specs/` is the source of truth; when prose and spec disagree, the spec wins.
- **Keep it current.** A change updates the documents it invalidates; stale documentation is a bug.
- **New documents land in a home above.** If none fits, propose a new home in an OpenSpec change first.

## Verification gate

`scripts/verify.sh` is the done-gate; [`../CONTRIBUTING.md`](../CONTRIBUTING.md) owns its step
list. By default its test phase runs the solution-wide `dotnet test` the gate has always used,
once per culture. Setting `LUNATE_VERIFY_JOBS=2` (or more) opts into a faster local mode: the
default-culture and `de-AT` passes run at the same time, each with a bounded pool of test
assemblies, while process- and environment-sensitive assemblies keep a serial lane inside a
pass. That shaves about two minutes off the test phase locally; CI keeps the default path
because timing-sensitive suites flaked on the shared Windows runner under the pooled load. The
passes are independent processes, so overlapping them changes the wall time, not what the gate
checks.

## Blame and the one-time reformat

`.git-blame-ignore-revs` lists the one-time CSharpier reformat (and future bulk formatting commits). GitHub honors it automatically; configure git once to use it locally:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```
