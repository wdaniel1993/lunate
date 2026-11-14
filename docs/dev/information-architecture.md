# Documentation architecture for 0.4.0

Status: signed off 2026-11-10 — the architecture stands and the implementation lands as #749–#757 (S1–S9), in that order. Refs #614.

This page answers how Lunate's documentation is laid out in 0.4.0, and the short answer is: one user entry point, eight task groups, and one maintainer tree, with every page written as a pyramid and every runnable example checked.

## The governing thought

Lunate's documentation is organised around what a reader wants to do, not around the components that do it. A reader lands on one entry point, picks a task group, gets the short answer in the page's first paragraph, and follows one link to the page that owns the detail. Maintainer material does not compete with that path; it lives under `docs/dev/` and is linked only where a contributor needs it.

## Principles

1. **Two paths, one fact.** The user path is `docs/index.md` and the task pages it links to. The maintainer path is `docs/dev/`. A fact has exactly one home; the other path links to it.
2. **Pyramid per page.** The first paragraph says what the page answers and gives the short answer. Headings are tasks or statements. Reference tables, edge cases and exceptions come last. Every page ends with **Related** links.
3. **Move, do not rewrite.** A slice moves existing text to its owning page and adds only the transition sentence the new structure needs. Claims are not re-derived in this reorganisation.
4. **Generated before hand-written.** Anything that mirrors code or data — settings keys, keybindings, the release version, CLI examples and config examples — is generated from or checked against its real source.
5. **Checked before merged.** `scripts/docs-sync` and markdownlint stay green, and a new `scripts/docs-check` keeps links, redirects and runnable examples honest in every slice.

## Target layout

```text
docs/
  index.md                    the user entry point
  quickstart.md               install, first run, first task
  how-lunate-works.md         loop, tools, context, sessions, modes (no internals)
  usage.md                    the interactive session
  models-and-providers.md     choose a model, provider, profile and credential
  decisions.md                the optional decision layer
  export-copy-debug.md        /copy, /export, /debug, diagnostics
  terminal-setup.md           Windows, tmux/SSH, narrow terminals, shells
  configuration.md            how to configure, where files live, project scope
  agents-md.md                project instructions
  skills.md                   Agent Skills
  mcp-servers.md              the MCP client
  themes.md                   themes, the tokens and the theme file
  themes-and-keys.md          the /settings theme and remap rows
  print.md                    print mode
  json-events.md              the --json event stream
  acp.md                      editor integration over ACP
  ci.md                       scripted setup, trust, unattended runs
  cli.md                      command and option reference
  settings.md                 generated settings reference
  providers.md                providers, presets and model catalog
  keybindings.md              generated keybinding reference
  environment-variables.md    every environment variable Lunate reads
  security.md                 the user-facing safety page
  troubleshooting.md          first-run, model, terminal and session failures
  third-party-licences.md     legal notices
  extensions/                 authoring, contributions, packages, testing (as today)
  extensions-remote.md        out-of-process extensions
  extensions-remote.schema.json
  mcp-roslyn.md               the Roslyn MCP server
  media/                      images plus the tapes/fixtures that render them
  dev/
    README.md                 the documentation map and writing guideline
    guide.md                  the implementation plan (no AGENTS.md copy)
    architecture.md           component, pipeline, run and event diagrams
    spec/                     extensibility, export and diagnostics contracts
    spikes/                   spike code and reports
    eval/                     eval write-ups and audits
    distribution.md           release runbook
    media.md                  how the media set is captured and re-rendered
    information-architecture.md  this page
```

| Group | Pages | Owns |
| --- | --- | --- |
| Get started | `index.md`, `quickstart.md`, `how-lunate-works.md` | What Lunate is, install and first run, the mental model |
| Use it | `usage.md`, `models-and-providers.md`, `decisions.md`, `export-copy-debug.md`, `terminal-setup.md` | The daily session and the terminal |
| Customize | `configuration.md`, `agents-md.md`, `skills.md`, `mcp-servers.md`, `themes.md`, `themes-and-keys.md` | User-owned configuration and resources |
| Build on it | `extensions/`, `extensions-remote.md`, `mcp-roslyn.md` | Extension authoring, the wire contract, the C# tools from another host |
| Automate | `print.md`, `json-events.md`, `acp.md`, `ci.md` | One-shot, machine-readable, editor and unattended use |
| Reference | `cli.md`, `settings.md`, `providers.md`, `keybindings.md`, `environment-variables.md` | Look-up tables, generated where the source exists |
| Work safely | `security.md` | The user-facing posture; `../SECURITY.md` stays the reporting policy |
| Troubleshooting | `troubleshooting.md` | Symptom → cause → next step |
| Maintainer | `dev/README.md`, `dev/guide.md`, `dev/architecture.md`, `dev/spec/`, `dev/spikes/`, `dev/eval/`, `dev/distribution.md`, `dev/media.md` | Plans, contracts, evidence and release operations |

`README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md` and `AGENTS.md` stay at the repository root. The README remains the project landing page; `docs/index.md` becomes the documentation entry point and the README's "Get started" and capability links point into it.

## Old path → new path

| Today | 0.4.0 home | Notes |
| --- | --- | --- |
| `docs/README.md` | `docs/dev/README.md` | Stub at the old path for one release; `AGENTS.md` keeps working |
| `docs/guide.md` | `docs/dev/guide.md` | The AGENTS.md rule copy is removed now (see below); a stub stays at the old path |
| `docs/architecture.md` | `docs/dev/architecture.md` | No stub; maintainer page |
| `docs/distribution.md` | `docs/dev/distribution.md` | No stub; maintainer page |
| `docs/spec/extensibility.md` | `docs/dev/spec/extensibility.md` | Extension pages link to it as the normative contract |
| `docs/spec/export.md` | `docs/dev/spec/export.md` | The user-facing workflow moves to `export-copy-debug.md` |
| `docs/spec/diagnostics.md` | `docs/dev/spec/diagnostics.md` | The user-facing workflow moves to `export-copy-debug.md` |
| `docs/spikes/**` | `docs/dev/spikes/**` | Pure rename; ADR links stay historical by the records rule |
| `docs/eval/**` | `docs/dev/eval/**` | Pure rename; instruction pages are updated in the same slice |
| `docs/media/README.md` | `docs/dev/media.md` | Images, tapes and fixtures stay under `docs/media/` |
| `docs/usage.md` | `docs/usage.md` | Stays the session page; export/copy/debug, project instructions, MCP, skills and terminal bands leave |
| `docs/first-run.md` | `quickstart.md`, `models-and-providers.md`, `decisions.md`, `ci.md` | The wizard walk stays in quickstart; setup flags go to `ci.md`; a stub stays |
| `docs/configuration.md` | `configuration.md`, `settings.md`, `decisions.md`, `providers.md` | The generated table moves to `settings.md`; provider-entry reference moves to `providers.md`; decision sections move to `decisions.md` |
| `docs/mcp-roslyn.md` | `docs/mcp-roslyn.md` | Stays; rewritten to the page template in its slice |
| `docs/extensions/**` | `docs/extensions/**` | Stays; only links to `docs/dev/spec/` change |
| `docs/extensions-remote.md` | unchanged | Build-on-it page |
| `docs/extensions-remote.schema.json` | unchanged | Lives beside the page that cites it |
| `docs/third-party-licences.md` | unchanged | Linked from the Reference group and the maintainer map |
| `docs/media/**` (assets) | unchanged | Page images keep stable URLs |

New pages are assembled from existing text; the only new prose is the transition sentence in each first paragraph and the two Get-started pages (`quickstart.md` distils README steps 1–3 and the wizard walk, `how-lunate-works.md` distils the guide's loop, tools, context and modes).

## Redirect and link plan

There is no documentation site: GitHub renders the Markdown, and a moved file's old URL returns 404. The plan is therefore in-repo links plus explicit stubs.

- **In-repo links** are updated in the same slice that moves a page. `scripts/docs-check` resolves every relative link and every `#anchor` before the slice can merge.
- **User-path pages that move or split** keep a four-line stub at the old path for the 0.4.x cycle:

  ```markdown
  <!-- moved: models-and-providers.md -->
  # Moved

  This page is now [Models and providers](models-and-providers.md). Update your bookmarks.
  ```

  `scripts/docs-check` asserts the target exists and the stub carries no other content. The stubs are removed in the release after 0.4.0.
- **Maintainer pages** get no stub. They were never a public contract, and the records rule means ADRs and archived changes keep the paths that were true when they were written.
- **Anchors.** A heading rewritten from a noun to a task is allowed to change its anchor only if every in-repo link is updated in the same slice. README and issue links are checked by the link checker; no HTML anchor shims are added.
- **Future site.** `docs/docs.json` carries the same `navigation` and `redirects` shape as Pi's manifest, so a site can consume it later without a second map.

`AGENTS.md` gets a path-only edit in the maintainer slice: the two lines that name `docs/guide.md` and `docs/README.md` point at their new homes. No rule text and no formatting changes.

## Page template

Every user-path page has this shape:

1. A task-shaped title.
2. A first paragraph: one sentence saying what the page answers, one sentence giving the short answer.
3. Sections headed by the task or the statement ("Switch the model for one session", not "Models").
4. Details, exceptions and reference tables last.
5. A **Related** section of links to the pages that own the neighbouring facts.

### Worked example: the Roslyn MCP page

The facts come from today's `mcp-roslyn.md`; only the shape changes.

````markdown
# Use Lunate's C# tools from another MCP host

This page answers how to serve Lunate's C# tools over MCP to an editor or another agent, and the short answer is `lunate mcp roslyn` on stdio: five read-only tools, no model, no session and no approvals.

## Run the server

Add it to the host's MCP configuration and start it from the repository root:

```json
{ "mcpServers": { "lunate-roslyn": { "command": "lunate", "args": ["mcp", "roslyn"] } } }
```

The `roslyn` manifest activates only when a direct child of the selected root is a solution or project; roots are re-read when the client announces a change.

## Know which tools it serves

| Tool | What it answers |
| --- | --- |
| `cs_diagnostics` | Compiler errors and warnings for a file, the changed files or the solution |
| `cs_find_symbol` | Definition and signature for a type, member or namespace |
| `cs_find_references` | Usage sites of an exactly resolved symbol |
| `cs_outline` | A file's types and member signatures |
| `cs_rename` | A solution-wide rename **plan** as per-file edits |

All five declare `readOnlyHint`; `cs_rename` changes nothing on disk.

## Expect these limits

- A root with no solution lists no tools, the same answer the chat path gives.
- A changed project or solution file needs a restart; source edits are re-read on the next call.
- There is no approval panel, write path, agent surface, nested server or file watcher.
- stdout carries JSON-RPC frames only.

## Related

- [The MCP client](mcp-servers.md) — the other direction, servers Lunate consumes
- [Extension contract](dev/spec/extensibility.md) — the built-in manifest rule
- [C# tools in a session](usage.md#c-the-compiler-agrees-with) — the same tools inside Lunate
````

## Runnable docs

The existing tagged-snippet loader stays, and the check grows in four layers:

1. **Generated regions.** `settings.md` keeps the `<!-- docs-sync: settings -->` region, `keybindings.md` takes the `<!-- keys:start -->` region, and the README keeps the version region. `scripts/docs-sync` owns all three.
2. **Source snippets.** `<!-- snippet: <path> -->` continues to expand a real file, so a config or project example cannot drift from the file it shows.
3. **Config examples.** A new `<!-- config: settings|mcp|providers|extension -->` marker above a fenced JSON block makes `scripts/docs-check` parse the block against the same schema file the product reads.
4. **CLI examples.** A new `<!-- run: offline|help|no -->` marker above a fenced `bash` block records how the command is checked. `offline` runs it in `$TMPDIR` with an isolated `LUNATE_HOME` and `HOME`; `help` runs `lunate <command> --help` and checks the exit status; `no` requires a one-line reason (network, paid request, interactive terminal). In the full CI profile the same checker compares every option named in `cli.md` against that command's `--help` output.

`docs/docs.json` is the machine-readable map: `navigation`, `redirects` and `proseOnly`. `scripts/docs-check --check` runs in the always-on docs job and enforces:

- every navigation path exists and every user-path page appears exactly once;
- every relative link and anchor resolves;
- every redirect source is a valid stub or a maintainer rename;
- every user-path page carries at least one `snippet`, `config` or `run` marker, or appears in `proseOnly` with a reason;
- the section lists in `docs/index.md` match `navigation` through a `<!-- docs-sync: navigation -->` region.

`scripts/docs-check --self-test` covers the failure paths, and `--run` executes the `offline` and `help` blocks in the full profile after the binary is published. No network command runs in CI.

## Slices

After sign-off, each row becomes one sub-issue and one pull request. Every slice leaves `docs-sync`, markdownlint and `docs-check` green and updates `docs/docs.json` in the same commit.

| Slice | Title | Scope | Exit |
| --- | --- | --- | --- |
| S1 | `docs(check): add the documentation checker and navigation manifest` | `scripts/docs-check` with self-tests, `docs/docs.json` for today's tree, docs/lint and verify wiring | Checker green on today's docs; no page moves  — #749 |
| S2 | `docs(reference): split the generated settings and keybinding pages` | `settings.md`, `keybindings.md`; move the `docs-sync` regions and update `GuideKeyTableTests` and the TUI comments | Settings and keys generated in their reference pages; guide no longer carries them  — #750 |
| S3 | `docs(dev): move the plan, architecture and release runbook` | `docs/dev/README.md`, `guide.md`, `architecture.md`, `distribution.md`, `media.md`; update instruction links; minimal `AGENTS.md` path edit | Maintainer pages resolve; the moved guide keeps its link to AGENTS.md  — #751 |
| S4 | `docs(dev): move the specs, spikes and eval evidence` | `docs/dev/spec/`, `docs/dev/spikes/`, `docs/dev/eval/`; update links from extension pages, README, CONTRIBUTING and the guide | No in-repo link to the old paths except records  — #752 |
| S5 | `docs(use): split the first run and the session extras` | `quickstart.md`, `models-and-providers.md`, `decisions.md`, `export-copy-debug.md`, `terminal-setup.md`; trim `usage.md`; stubs for `first-run.md` | Each new page answers one task; `usage.md` under about 300 lines  — #753 |
| S6 | `docs(customize): give each customization its own page` | `configuration.md` (tasks), `agents-md.md`, `skills.md`, `mcp-servers.md`, `themes-and-keys.md`; `settings.md` keeps the table | Configuration reads as tasks; generated reference stays in Reference  — #754 |
| S7 | `docs(automate): add the print, JSON, ACP and CI pages` | `print.md`, `json-events.md`, `acp.md`, `ci.md`, `cli.md`, `providers.md`, `environment-variables.md` | Automate and Reference groups complete; CLI options checked against `--help`  — #755 |
| S8 | `docs(security): add the safety and troubleshooting pages` | `security.md`, `troubleshooting.md`; link `SECURITY.md` as the reporting policy | A user can find the posture and a next step without reading the guide  — #756 |
| S9 | `docs(index): make docs/index.md the entry point` | `index.md`, `quickstart.md`, `how-lunate-works.md`; generated navigation region; README links; the moved-page stubs stay until the next release | `docs/index.md` leads with the governing thought and every group resolves  — #757 |

S1 must land first; S2 before S3 because the key table leaves the guide. S3 and S4 are independent of S5–S8. S9 is last because it links only to pages that already exist.

## Done-when mapping

| Issue done-when | Where it lands |
| --- | --- |
| The design is signed off and sliced | This page plus the S1–S9 sub-issues filed after sign-off |
| Every user-path page passes the runnable-docs check | `scripts/docs-check` in S1; each page gains its markers in its own slice |
| Maintainer material lives under `docs/dev/` | S3 and S4 |
| `docs/index.md` leads with the governing thought and links down | S9 |
| `docs-sync` and markdownlint are green | Every slice; `docs-check` is added to the same gate |

## Ruling on the duplicated AGENTS.md rules

`docs/guide.md` used to reproduce the AGENTS.md rules in a fenced block. This change removes that copy and links to [`AGENTS.md`](../../AGENTS.md) instead, so the guide cannot drift from the source of truth. The guide remains the implementation plan; its own sections (testing, security, decisions) stay.

## Decisions taken

- **No documentation site.** GitHub Markdown is the renderer, so redirects are stubs plus a manifest for a future site.
- **Flat user path.** User pages stay directly under `docs/` (as Pi does) instead of nested group folders; the groups exist in `docs/index.md` and `docs/docs.json`, not in the filesystem. This keeps every moved page's URL short and the diff small.
- **`SECURITY.md` stays at the root.** The new `security.md` is the user-facing posture; the policy and reporting process remain in `SECURITY.md`.
- **`spec/extensibility.md` moves to `docs/dev/`.** It remains the normative contract and is linked from the extension pages; moving it follows the issue's maintainer-material list.
- **The checker is Python 3 standard library**, like `scripts/docs-sync`, so every CI runner and macOS can run it without a package.

## Related

- [Documentation map](README.md) — the current home for each document kind
- [Contributing](../../CONTRIBUTING.md) — the build and verify gate
- [Agent rules](../../AGENTS.md) — the rules that govern this repository
