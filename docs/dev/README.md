# Documentation map

Lunate keeps each kind of document in exactly one home. Start here, then jump to the document that owns the fact you need.

| Document kind | Home | What it owns |
| --- | --- | --- |
| Project entry point | [`../../README.md`](../../README.md) | What Lunate is, why it exists, install, the three-step start and what it can do |
| Documentation entry point | [`../index.md`](../index.md) | The user path: every task group, and the page that owns each task |
| Usage | [`../usage.md`](../usage.md) | The interactive session: input, turns, tools, approvals, thinking, forks and long sessions, and trust |
| Project instructions | [`../agents-md.md`](../agents-md.md) | The `AGENTS.md` chain from the repository root to the launch directory, and what reaches the system prompt |
| Skills | [`../skills.md`](../skills.md) | Agent Skills: the scopes Lunate reads, `/skills`, `/skill:<name>`, the index budget and the trust decision |
| MCP servers | [`../mcp-servers.md`](../mcp-servers.md) | The MCP client: where `mcpServers` is read, how a server starts, and what its child process inherits |
| Themes | [`../themes.md`](../themes.md) | The built-in themes, the ten semantic tokens, the user `theme.json` and the colour downgrade |
| Themes and keys | [`../themes-and-keys.md`](../themes-and-keys.md) | The `/settings` rows for the theme and the key remaps, and why `keys.json` stays the only declaration |
| Execution environments | [`../execution-environments.md`](../execution-environments.md) | Run tool calls in a container: where commands and file tools run, the Docker/Podman flag and settings, what is mounted, and the measured cost and platform limits |
| Configuration | [`../configuration.md`](../configuration.md) | How to configure a session: decision profiles, custom provider entries, thinking, the welcome header and update check, and recording |
| Print mode | [`../print.md`](../print.md) | One prompt to completion: the final answer as the only thing on stdout, the exit codes, the approval mapping and the cost and time caps |
| JSON event stream | [`../json-events.md`](../json-events.md) | The `--json` line shapes: the stamped envelope, every event type with its fields, and the usage, cache and limit fields |
| Editors over ACP | [`../acp.md`](../acp.md) | `lunate --acp`: the Agent Client Protocol over stdio, what an editor gets, and what the mode refuses |
| CI and unattended runs | [`../ci.md`](../ci.md) | `lunate setup` without a TTY, trust by digest, and print mode in a pipeline |
| Command and option reference | [`../cli.md`](../cli.md) | Every command and flag, checked against `--help` before the docs merge |
| Providers and the model catalog | [`../providers.md`](../providers.md) | The twelve presets, the five API families, custom provider entries, and how a model id resolves through the catalog |
| Environment variables | [`../environment-variables.md`](../environment-variables.md) | Every environment variable Lunate reads, and which settings key it overrides |
| Settings reference | [`../settings.md`](../settings.md) | Every settings key with its type, default, scope and allowed values, generated from the settings schema so it cannot list a key the store refuses |
| Keybindings reference | [`../keybindings.md`](../keybindings.md) | Every prompt binding, generated from the keys file the router and the in-app help read so it cannot describe a key the product does not have |
| Quickstart | [`../quickstart.md`](../quickstart.md) | Install, the first run and the setup wizard, and the first task |
| How Lunate works | [`../how-lunate-works.md`](../how-lunate-works.md) | The mental model: the turn loop, the tools, the context, sessions and the three modes |
| Models and providers | [`../models-and-providers.md`](../models-and-providers.md) | Choosing a provider, `lunate auth`, the `/auth` picker, provider instances and credentials, profiles, custom OpenAI-compatible endpoints and the user's own model catalog |
| Decisions | [`../decisions.md`](../decisions.md) | The optional decision layer: its profile, approvals, routing, the decide tool, question suggestions and the System One transport |
| Copy, export and debug | [`../export-copy-debug.md`](../export-copy-debug.md) | `/copy`, `/export` and `/debug`: taking an answer, the session or a diagnostic bundle out |
| Terminal setup | [`../terminal-setup.md`](../terminal-setup.md) | Supported terminals, Windows and tmux/SSH, narrow terminals and the shell the `bash` tool resolves |
| Contributor guide | [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) | Prerequisites, build, the verify gate, the repository map, the OpenSpec workflow |
| Security policy | [`../../SECURITY.md`](../../SECURITY.md) | How to report a vulnerability |
| Security and safety | [`../security.md`](../security.md) | The user-facing posture: approvals, the workspace boundary, the bash guard, the container boundary, trust, secrets and what is out of scope |
| Troubleshooting | [`../troubleshooting.md`](../troubleshooting.md) | Symptom → cause → next step for first-run, model, terminal, container and session failures |
| Changelog | [`../../CHANGELOG.md`](../../CHANGELOG.md) | Notable changes, one section per release |
| Changelog fragments | [`../../changes/README.md`](../../changes/README.md) | The release-note rule: a pull request writes `changes/<pr-number>.md`, the release pull request assembles `CHANGELOG.md` |
| Plan and status | [`guide.md`](guide.md) | The implementation plan, phases and roadmap |
| Documentation architecture | [`information-architecture.md`](information-architecture.md) | The 0.4.0 target layout, page template, runnable-docs check and the S1–S9 slice plan |
| Architecture overview | [`architecture.md`](architecture.md) | Component map, model pipeline, one run and the event path, as diagrams |
| Extensibility spec | [`spec/extensibility.md`](spec/extensibility.md) | Contract and hooks, tool model, services, UI by mode, fitness suite (under ADR-0017) |
| Export contract | [`spec/export.md`](spec/export.md) | `/export` and `/copy`: destination, credential redaction, the HTML document's CSP and escaping guarantees |
| Diagnostic bundle | [`spec/diagnostics.md`](spec/diagnostics.md) | `/debug`: destination, sections, the event window, and what the bundle never carries |
| Extension authoring | [`extensions/`](../extensions/README.md) | Quickstart, manifest reference, single-file headers, per-surface API and constraints, lifecycle and trust, testing, troubleshooting |
| Installing packages | [`../extensions/packages.md`](../extensions/packages.md) | The bundle manifest, dependency resolution and its lock, the install record, both scopes, upgrade and uninstall, offline and private feeds, trust after install |
| Remote extensions | [`../extensions-remote.md`](../extensions-remote.md) | The out-of-process manifest and wire reference for extension authors (schema: [`schemas/extensions-remote.schema.json`](../../schemas/extensions-remote.schema.json)) |
| Roslyn MCP server | [`../mcp-roslyn.md`](../mcp-roslyn.md) | `lunate mcp roslyn`: the C# tools over stdio, and what the server leaves out by design |
| Distribution runbook | [`distribution.md`](distribution.md) | Install scripts, release assets, channel ops and the first-release checklist |
| Media set | [`media.md`](media.md) | The demo and feature GIFs, the build that captured them, and how to re-render them |
| Behaviour source of truth | [`../../openspec/specs/`](../../openspec/specs/) | Current behaviour, one folder per capability |
| In-flight proposals | [`../../openspec/changes/`](../../openspec/changes/) | Proposals, deltas, designs and tasks being implemented |
| Decisions with context | [`../../adr/`](../../adr/) | Durable architectural decisions, immutable once accepted |
| Evidence | [`spikes/`](spikes/) | Spike code and reports that back an ADR |
| Evaluation and audits | [`eval/`](eval/) | Head-to-head results, the first SWE-bench pilot and surface audits that back a positioning claim |

## Writing guideline

- **English-first.** User- and model-facing text is English; technical output is culture-invariant.
- **Linted.** `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`, `AGENTS.md`,
  `changes/**/*.md`, `docs/**/*.md` and `adr/**/*.md` are checked by markdownlint
  (`npx --yes markdownlint-cli2@0.23.3`, configured in `.markdownlint-cli2.yaml`). `openspec/` is
  tool-managed and excluded.
- **Pyramid per page.** A user-path page opens with one sentence saying what it answers and one
  giving the short answer, heads its sections with the task or the statement rather than a noun, and
  closes with a **Related** section of links to the pages that own the neighbouring facts. Details,
  edge cases and reference tables come last.
- **One home per fact.** Link to the owning document instead of copying it; a fact that exists twice drifts.
- **Generated regions are not hand-written.** `scripts/docs-sync` fills
  `<!-- keys:start --> … <!-- keys:end -->` in `keybindings.md` from `src/Lunate.Tui/keys.json`
  (the file the router and the in-app help read), `<!-- docs-sync: settings --> …` in `settings.md`
  from `src/Lunate.Coding/settings-schema.json` (the file `SettingsSchema` reads and tests pin to
  the parser's accepted keys), and `<!-- docs-sync: version --> …` in the README with the newest
  *published* stable release — the newest non-prerelease tag whose release attached its
  `SHA256SUMS`, the asset that exists exactly when the release does. A tag whose release never
  published is not a release, and the channel-manifest guard reads the same definition from
  [`../../scripts/published_release.py`](../../scripts/published_release.py). It also expands
  `<!-- snippet: <path> -->` into a fenced block
  taken from that real file — the marker owns the block below it. Edit the source, then run
  `scripts/docs-sync`; `--check` is what the gate runs, in the docs profile, in the always-on
  `Process` job (that job's checkout fetches the tags, which the version region reads), and in the
  tree checks — `tree-checks.yml` on every push to `main` and the merge queue's `tree-checks` job,
  both calling `scripts/tree-checks.sh` — a docs-only push skips the matrix, so the push is the one
  place a stale region had no gate at all without them.
- **Links, anchors and redirects are checked.** `scripts/docs-check --check` resolves every relative
  link and `#anchor` on the user and maintainer pages, validates every redirect stub, and keeps
  `docs/docs.json` honest: the manifest's `navigation` lists every user-path page exactly once, and
  `proseOnly` carries the pages that have no runnable example yet with a one-line reason.
- **The CLI reference is checked against `--help`.** In the `--run` pass `scripts/docs-check`
  executes the `<!-- run: help -->` block on `cli.md` and compares every long option in the page with
  the output in both directions, so an option the product does not accept and an option the page
  forgot both fail the gate.
- **Behaviour changes update the specs first.** `openspec/specs/` is the source of truth; when prose and spec disagree, the spec wins.
- **Keep it current.** A change updates the documents it invalidates; stale documentation is a bug.
- **New documents land in a home above.** If none fits, propose a new home in an OpenSpec change
  first, then add the page to `docs/docs.json` so the checker can see it.

## Verification gate

`scripts/verify.sh` is the done-gate; [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) owns its step
list. Its docs profile runs markdownlint, `scripts/docs-sync --check`, `scripts/docs-check --check`
and the OpenSpec tree; the full profile also runs `scripts/docs-check --run` against the published
binary after the runnable examples exist. By default its test phase runs the solution-wide
`dotnet test` the gate has always used, once per culture. Setting `LUNATE_VERIFY_JOBS=2` (or more)
opts into a faster local mode: the default-culture and `de-AT` passes run at the same time, each
with a bounded pool of test assemblies, while process- and environment-sensitive assemblies keep a
serial lane inside a pass. That shaves about two minutes off the test phase locally; CI keeps the
default path because timing-sensitive suites flaked on the shared Windows runner under the pooled
load. The passes are independent processes, so overlapping them changes the wall time, not what the
gate checks.

## Blame and the one-time reformat

`.git-blame-ignore-revs` lists the one-time CSharpier reformat (and future bulk formatting commits). GitHub honors it automatically; configure git once to use it locally:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```
