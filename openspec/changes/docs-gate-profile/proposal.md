## Why

A proposal pull request — `openspec/changes/<name>/**`, the largest group of docs-only changes — runs the full gate: a Release build, a single-file publish, the whole suite twice (default culture and `de-AT`), the startup budget, the install-script and `dotnet tool` roundtrips, formatting, the public API diff. About twenty minutes of it, on three operating systems plus the PowerShell twin, to check Markdown and spec deltas. And because those legs run thousands of tests that the diff cannot touch, such a pull request can go red on an unrelated flake, which costs another cycle and teaches the author to re-run until green.

The gate cannot simply narrow its paths: a document a test reads is a contract, and today two tests read the docs tree — `GuideKeyTableTests` parsed `docs/guide.md`'s key table, `ExtensionTemplateTests` read the authoring page, and `RemoteJsonSchemaTests` compared its generator against a schema published under `docs/`. So the cheap profile is unsound until code owns what those tests check. That is the first half of this change: move the key catalog into data, generate the table that mirrors it, generate the authoring page's snippet from the real template, and move a machine-readable schema out of the prose tree. The second half is the profile itself and the plan that selects it.

## What Changes

- **The keymap is data.** `src/Lunate.Tui/keys.json` carries every binding — a stable id, its key patterns, the routed intent, the context flags, the help text — and `docs/guide.md`'s table rows beside them. `KeyBindingCatalog` embeds the file and builds the same ordered rows; the router, the in-app help and the guide all read it. The 36 rows are byte-for-byte the old list in order, and the table rendered from the file equals the hand-written one byte for byte.
- **The docs are generated where they mirror code.** `scripts/docs-sync` (python3, standard library) fills `<!-- keys:start --> … <!-- keys:end -->` from `keys.json` and expands `<!-- snippet: <path> -->` into a fenced block taken from that file — the extension authoring page shows the template's real project references — and `--check` writes nothing and exits 1 on a diff. A test now checks the data (`KeysFileTests`) instead of parsing the page.
- **No test reads `docs/`.** The published remote-extension schema moves to `schemas/extensions-remote.schema.json` (a machine-readable contract, not prose); the template test's subject is the template file, including that each reference resolves; `scripts/no-docs-references.sh` fails when a project item or solution line names a docs path, when a test that resolves the repository root names one, or when a `File`/`Directory` call does — with an explicit, printed allowance for in-memory fixtures.
- **The gate has a prose profile.** `scripts/verify.sh --profile docs` runs markdownlint, `openspec validate --all --strict`, `scripts/docs-sync --check` and `scripts/no-docs-references.sh` — no build, no publish, no test pass, no startup budget, no API check — in seconds. `--profile full` stays the default and the gate as it was, and an unknown profile exits 2.
- **CI selects the profile, deny-by-default.** `scripts/ci-plan.sh` reads the pull request's `git diff --name-status` and answers `docs` only when every path is prose: `docs/`, `adr/`, `openspec/` or root-level Markdown. Anything else — source, tests, templates, and any `.github/workflows/` change — is `full`; a rename is read as both paths, and an empty or unreadable change set fails closed. The plan job runs the script's `--self-test` on every pull request. `ci-ok` keeps its name and stays the single aggregate, now failing closed when it cannot read the profile and refusing a run whose legs disagree with the plan.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-pipeline` — *Per-commit verification matrix*: a pull request whose changed paths are all prose runs the documentation profile on one runner instead of the full gate on three, and the profile is chosen deny-by-default by a committed script. *Aggregate merge gate*: the aggregate succeeds when the jobs the profile selected passed and the ones it did not were skipped, fails on a leg that ran anyway, and fails when it cannot read the profile.

## Impact

- `src/Lunate.Tui/keys.json`, `src/Lunate.Tui/Interaction/KeyBindingCatalog.cs` (new), `KeyBinding.cs` (the embedded list removed), `Lunate.Tui.csproj` (the embedded resource).
- `scripts/docs-sync`, `scripts/no-docs-references.sh`, `scripts/ci-plan.sh` (new), `scripts/verify.sh` (the profile), `.github/workflows/ci.yml` (the plan job, the docs leg, the aggregate), `CONTRIBUTING.md` and `docs/README.md`.
- Tests: `KeysFileTests` (new), `GuideKeyTable.cs` / `GuideKeyTableTests` (deleted), `ExtensionTemplateTests`, `RemoteJsonSchemaTests`.
- Docs: `docs/guide.md` (markers only beyond today's table), `docs/extensions/README.md` (a generated snippet), `docs/extensions-remote.md` (the schema link), `templates/extension/TemplateExtension/TemplateExtension.csproj` (region markers).
- No product behaviour change: the binding set, the intents, the contexts, the help text, the legend and the router's order are identical, and the app's public API is untouched. No new package reference; the docs profile needs `python3` and the `openspec` CLI, both present on the runners (pinned in CI).
