## 1. Keymap data (product, no behaviour change)

- [x] 1.1 Red: `keys.json` declares 36 bindings whose rows equal the old embedded list in order, same patterns (absent modifier = unconstrained), intents, contexts and descriptions; the catalog expands every declared pattern.
- [x] 1.2 Red: a guide row that names a binding renders exactly its `keys` cell (merged when two bindings share a spelling), and every binding is documented by exactly one row.
- [x] 1.3 Red: a row without bindings carries a `reason` and none of its keys is a binding's spelling; a missing `guideRows` array, a missing id, an unknown intent/context/kind each fail with the entry named.
- [x] 1.4 `KeyBindingCatalog` reads the embedded resource (`Lunate.Tui.keys.json`) and throws a named error when it is missing or unreadable; `Lunate.Tui.csproj` embeds `keys.json`.
- [x] 1.5 Gate: `Lunate.Tui.Tests` green (1,762 / 0), and the regenerated table is byte-identical to the page's current one.

## 2. Generated docs regions

- [x] 2.1 `scripts/docs-sync` replaces the `<!-- keys:start -->` … `<!-- keys:end -->` region of `docs/guide.md` from `src/Lunate.Tui/keys.json`, and `--check` writes nothing and exits 1 on a stale page.
- [x] 2.2 `<!-- snippet: <path> -->` expands into a fenced block taken from the real file, or from its `snippet:start`/`snippet:end` region, dedented; the marker stays so a second run is a no-op and a source edit flows through.
- [x] 2.3 Refusals: a missing or duplicated keys marker, a missing snippet source, an unknown fence extension, a non-relative path, CRLF line endings, and a malformed marker at the start of a line — each names the page (and line) and the reason.
- [x] 2.4 `--self-test` covers generation, idempotency, a stale page, a missing marker, a missing source, an unknown extension, prose that mentions the syntax and a malformed marker (10 cases).
- [x] 2.5 The authoring page shows the template's real `ItemGroup` through a snippet, with `snippet:start`/`snippet:end` markers in `TemplateExtension.csproj`; the hand-written block is gone.
- [x] 2.6 Gate: `scripts/docs-sync --check` clean on the tree, and `git diff docs/guide.md` shows the two marker lines and nothing else.

## 3. No test reads the docs tree

- [x] 3.1 `GuideKeyTable.cs` and `GuideKeyTableTests` are deleted; their checks live in `KeysFileTests` over `keys.json`.
- [x] 3.2 `ExtensionTemplateTests.The_authoring_page_shows_the_same_shape` is deleted; the template test keeps the attribute assertions and gains "every reference resolves to a project".
- [x] 3.3 The published schema moves to `schemas/extensions-remote.schema.json`, and `RemoteJsonSchemaTests` reads it there; the two links follow.
- [x] 3.4 `scripts/no-docs-references.sh` fails on a project/solution item naming a docs path, a root-resolving test naming one, and a `File`/`Directory` call on one; it allows (and prints) an in-memory fixture path, and fails when it scanned no test source.
- [x] 3.5 `--self-test` covers eight cases: clean tree, csproj, props, root read, direct read, fixture allowance, rendered string, empty tree.
- [x] 3.6 Gate: the check passes on this tree — 627 test files scanned, three fixture lines allowed and printed.

## 4. The docs profile and the CI plan

- [x] 4.1 `scripts/verify.sh --profile docs` runs the four prose steps and nothing else; `--profile full` is the default and unchanged; an unknown or misspelled profile exits 2.
- [x] 4.2 The profile fails with a named message when `python3`, `node` or the `openspec` CLI is missing.
- [x] 4.3 `scripts/ci-plan.sh` decides `docs` only for `docs/`, `adr/`, `openspec/` and root-level `*.md`; renames are read as both paths, deletions and empty change sets fail closed; `--self-test` covers twelve cases.
- [x] 4.4 `ci.yml`: a `plan` job (self-tests + decision), `needs: plan` on `verify` and `verify-powershell`, a `verify-docs` job, and `ci-ok` keeping its name while failing closed on an unreadable profile or legs that disagree with it.
- [x] 4.5 `CONTRIBUTING.md` and `docs/README.md` document the profile, the plan and the generated regions.
- [x] 4.6 Gate: the docs profile on this tree is green in **5.3 s**; `openspec validate --all --strict` is 19 passed / 0 failed; `Lunate.Coding.Tests` 1,731 / 0 and `Lunate.Extensibility.Remote.Tests` 76 / 0.

## 5. Review

- [x] 5.1 Archive this change as the final commit — `ci-ok` was green on the implementation head (`431bee97`: plan=full, three verify legs and verify-powershell passed, verify-docs skipped by design); the archive commit follows as the last commit before merge.
