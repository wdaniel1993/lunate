## Context

Two gates exist and are commonly confused:

- **Fast gate** (~3–5 min): `dotnet csharpier check .`, `openspec validate <change> --strict`, a Release build, and the focused test classes for what changed.
- **Full gate** (~20 min locally, four legs in CI): `scripts/verify.sh` (`scripts/verify.ps1` on Windows) — build, every suite, both culture passes, single-file publish, performance budgets, public API, formatting and documentation lint.

`AGENTS.md` names only the full gate, and names it as the condition for "done". Read literally, every intermediate commit needs it.

## Decision

`AGENTS.md` line 19 is replaced with three rules. The exact text, for approval:

```markdown
- Iterate behind the fast gate: `dotnet csharpier check .`, `openspec validate <change> --strict`, a Release build, and the test classes that cover what you touched.
- `scripts/verify.sh` (Windows: `scripts/verify.ps1`) must pass before a change is marked ready for review or done: build, tests, single-file publish, performance budgets, public API, formatting (CSharpier) and documentation lint checks.
- `ci-ok` must be green before a merge.
```

The full gate's content is unchanged; only the moment it is required changes.

## Alternatives considered

- **Leave the text and rely on convention.** The convention already failed: two agents read the same line differently in the same week, and the ambiguity pushed work into long serial gate runs.
- **Make the fast gate a script.** Tempting, but the four commands take different arguments per change; a script would need the change name, the test classes and the project, and would be wrong more often than the one-line list is unclear.

## Risks

A change could be marked ready with a fast gate only if the author ignores the second rule. The CI ruleset already requires `ci-ok`, so the merge stays guarded; the rule that changes is the one about *asking for review*, which a reviewer can check in one command.
