# Documentation map

Lunate keeps each kind of document in exactly one home. Start here, then jump to the document that owns the fact you need.

| Document kind | Home | What it owns |
| --- | --- | --- |
| Project entry point | [`../README.md`](../README.md) | What Lunate is, prerequisites, quick start, repository map |
| Plan and status | [`guide.md`](guide.md) | The implementation plan, phases and roadmap |
| Architecture overview | [`architecture.md`](architecture.md) | Component map, model pipeline, one run and the event path, as diagrams |
| Extensibility spec | [`spec/extensibility.md`](spec/extensibility.md) | Contract and hooks, tool model, services, UI by mode, fitness suite (under ADR-0017) |
| Behaviour source of truth | [`../openspec/specs/`](../openspec/specs/) | Current behaviour, one folder per capability |
| In-flight proposals | [`../openspec/changes/`](../openspec/changes/) | Proposals, deltas, designs and tasks being implemented |
| Decisions with context | [`../adr/`](../adr/) | Durable architectural decisions, immutable once accepted |
| Evidence | [`spikes/`](spikes/) | Spike code and reports that back an ADR |

## Writing guideline

- **English-first.** User- and model-facing text is English; technical output is culture-invariant.
- **Linted.** `README.md`, `AGENTS.md`, `docs/**/*.md` and `adr/**/*.md` are checked by markdownlint (`npx --yes markdownlint-cli2@0.23.3`, configured in `.markdownlint-cli2.yaml`). `openspec/` is tool-managed and excluded.
- **One home per fact.** Link to the owning document instead of copying it; a fact that exists twice drifts.
- **Behaviour changes update the specs first.** `openspec/specs/` is the source of truth; when prose and spec disagree, the spec wins.
- **Keep it current.** A change updates the documents it invalidates; stale documentation is a bug.
- **New documents land in a home above.** If none fits, propose a new home in an OpenSpec change first.

## Blame and the one-time reformat

`.git-blame-ignore-revs` lists the one-time CSharpier reformat (and future bulk formatting commits). GitHub honors it automatically; configure git once to use it locally:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```
