## Context

`docs/guide.md` is the product's plan document, and its key table is the legend a user reads: the same bindings the router has, in the same spelling the help overlay renders. That made it worth policing, so `GuideKeyTableTests` parsed the page in both directions — a binding with no row failed, a row nothing routes failed unless it was in a named exemption list. The cost only showed up once CI grew a plan: a test that reads a page makes the page a build input, so no cheap profile for prose could be sound, and the test itself would go red on any pull request that touched the table (including one that only reformats it).

The same shape existed twice more. `ExtensionTemplateTests` read the authoring page to prove the page showed the template's `Private="false"` / `ExcludeAssets="runtime"` references, and `RemoteJsonSchemaTests` compared its generator against a schema published under `docs/`. Three different copies of "a test reads a document", three different reasons the docs tree could not be treated as prose.

`docs/README.md` already states the rule that resolves this: *one home per fact; a fact that exists twice drifts.* A table that mirrors code should be generated from the code's data, and a page that shows a file's contents should include the file.

## Goals and non-goals

**Goals.** A prose pull request proves what prose can break, in minutes: the generated regions match their sources, nothing under `docs/` is a build input or a test's subject, the Markdown lints, and the OpenSpec tree validates strictly. The full gate stays exactly what it was, byte for byte, for every other pull request. No product behaviour change.

**Non-goals.** Making the docs profile cover code (it does not build, and must not start); adding OpenSpec validation to the full gate (it would cost every code pull request a tool it does not need); a user-facing keybindings file (P8) — this change only makes the data file the place that file can build on.

## Where the keymap lives

`keys.json` carries two lists:

- `bindings`: the ordered rows the router uses — `id`, `keys` (one or more key patterns, each `{kind, ctrl, alt, shift, text}` with absent modifiers unconstrained, which is how `Escape` matches with any modifiers held), `intent`, `context` and `description`. `KeyBindingCatalog` expands each entry into one `KeyBinding` per pattern, in file order, so the row list, the tie-breaking and the help overlay are what they were.
- `guideRows`: the guide's table rows in page order — `keys` (the spellings the cell shows), the `bindings` the row documents, and, for a row that documents a key nothing routes, a `reason`. A row with `bindings` must render exactly its `keys` (in row order, merged when two bindings share a spelling); a row without them is the successor of the old exemption table, and its reason is required.

The alternative — letting the document keep the table and having the profile skip the check — was rejected: it would have left the legend unguarded for prose pull requests, which is precisely the class of change that edits it.

A stable id per row is what a future user bindings file overrides, so the ids are semantic (`newline.ctrl-j`, `ctrl-c.interrupt.turn`) rather than positional. Rows that share an intent stay separate entries because their contexts differ (`Enter` submits when idle, runs a bash line in bash mode, queues while a turn runs); merging them would have changed the row order and the overlay.

## Generation, and what makes it fail

`scripts/docs-sync` is python3 with the standard library, because every runner and macOS ship it and the alternative (a Node or .NET tool) would put a build or an npm install in the profile that exists to avoid them.

- **The keys region is mandatory.** `docs/guide.md` must carry one `<!-- keys:start -->` / `<!-- keys:end -->` pair; a missing or duplicated marker is an error, not a page without a generated region. That is the vacuity guard the deleted test had.
- **A snippet marker owns the fenced block below it.** The marker line stays in the page, so a second run is a no-op and an edit to the source flows through on the next run. The block is the whole file, or the lines between `snippet:start` / `snippet:end` if the source carries them, dedented by the region's common indentation so a nested `ItemGroup` reads as a block.
- **The fence language comes from the extension**, from a closed map; an unknown extension fails with a message naming the map rather than emitting a bare fence.
- **CRLF is refused**, naming the file: a page checked out with CRLF would read as stale on every run, and the repository normalises to LF.
- `--check` renders every page in memory, prints a line-level diff for each stale one, and writes nothing.

The guide's table is generated from the data and the data is checked against the catalog (`KeysFileTests`), so the chain is closed at both ends: a binding cannot lose its row, a row cannot name a missing binding, and a cell cannot drift from the spelling the overlay renders. No test reads the page.

## Untangling the other two reads

- **The published schema moves to `schemas/`.** It is generated from the DTOs by `RemoteJsonSchema.Build()` and compared by a test; a prose directory is the wrong home for a machine-readable contract, and keeping it under `docs/` would have meant either a portable-profile blind spot for one file or a docs path the guard has to excuse. The page links to it at its new home.
- **The template test keeps its subject.** `The_authoring_page_shows_the_same_shape` is deleted; the page's copy is a generated snippet now. The template test gains the check that every `ProjectReference` it declares resolves to a project that exists — the template is outside the solution, so a moved project would otherwise only surface when an author copied it. A full `dotnet build` of the template inside the unit suite was considered and rejected: it would restore packages and write MSBuild artifacts from a test process on all three legs, and the drift it would catch (a reference that no longer resolves, an attribute that stopped compiling) is caught by the attribute assertions plus the resolution check without that cost.
- **The guard is a script, not a review habit.** `scripts/no-docs-references.sh` fails on the three shapes a docs read takes and prints one line per allowed fixture path, so the exception the autocomplete composition test needs (a `docs/guide.md` written into a fake workspace) is visible every run instead of being a silent exclusion. It also fails when it scanned no test source at all, because a scan that finds nothing proves nothing.

## The profile and the plan

`--profile docs` is a closed set of four steps, fastest first: `docs-sync --check` (0.03 s), `no-docs-references.sh` (1.3 s), markdownlint (0.8 s), `openspec validate --all --strict` (0.6 s). Measured end to end on macOS: **5.3 s**. The startup budget, the publish, the culture passes and the API diff are absent by construction, not skipped by a flag, so no code path in the profile can reach them.

`scripts/ci-plan.sh` reads `git diff --name-status -M` and answers `docs` only when every path it sees is prose. Renames are read as both of their paths, so `docs/page.md → src/page.md` and the reverse both select the full gate; a deletion inside the allow-list is prose, a deletion outside it is not; an empty change set, a line without a path, or a missing file fails closed to `full`. The allow-list is `docs/`, `adr/`, `openspec/` and root-level `*.md` — a `*.md` inside a code folder is that folder's README and travels with the code.

CI wiring: a `plan` job (checkout with full history, the three tooling self-tests, then the decision) publishes `profile`; `verify` and `verify-powershell` require `profile == 'full'`, `verify-docs` requires `'docs'`, and `ci-ok` keeps its name and reads the profile. `ci-ok` fails when the plan job failed, when the profile is not one of the two, when a selected leg did not succeed, and when a leg ran that the profile did not select — the last two because a plan that lies about what ran is worse than a red run.

The docs leg installs the `openspec` CLI at the version the gate was checked against (`@fission-ai/openspec@1.14.0`) into a temporary prefix, the way it already pins `markdownlint-cli2`: a runner image's floating CLI would decide whether a proposal validates.

`scripts/verify.ps1` deliberately gains no docs profile. It exists to run the Windows twin of the full gate; a prose pull request does not run it at all, and duplicating four cross-platform steps into PowerShell would add a second implementation of a check that has one.

## Risks

- **A prose pull request could still break the build if something under `docs/` becomes an input.** That is the guard's whole job, and it runs in the profile it protects; the check also covers `*.props` and `*.targets`, so a `Directory.Build.props` include cannot slip past.
- **The allow-list can rot.** It is four prefixes and a suffix, in one function with twelve self-test cases, and the failure mode of a missing entry is `full` (slow), not `docs` (wrong).
- **The generated table could lose information.** The regeneration was done until `git diff` showed the marker lines and nothing else, and the data move was verified row by row against the old list.
