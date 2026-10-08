## ADDED Requirements

### Requirement: C# references tool
The optional `cs_find_references` tool SHALL list all source references (usage locations; declaration sites excluded and reported separately) for an exactly resolved symbol — a simple name only when unique, a dotted container path for disambiguation — with file, 1-based line and column, bounded with a truncation flag. Ambiguous names SHALL return candidates with a disambiguation hint, never a guess; metadata-only symbols SHALL return an explanatory message rather than an error; the tool SHALL be read-only.

#### Scenario: References across projects are found
- **GIVEN** a loaded multi-project fixture and a symbol used in both projects
- **WHEN** `cs_find_references` searches for it
- **THEN** usage locations from both projects are listed in stable order, the declaration is reported once separately, and a second run is identical

#### Scenario: Ambiguity is a hint, not a guess
- **GIVEN** two types with a member of the same simple name
- **WHEN** the simple name is searched
- **THEN** the result lists candidates with a hint to use a dotted path, and no references

### Requirement: C# outline tool
The optional `cs_outline` tool SHALL list one file's types and member signatures without bodies — kind, container path, name, declaration signature and 1-based line, in source order, bounded with a truncation flag — and SHALL NOT require a loaded solution (syntax-level; works when no solution or SDK is available). A missing or unreadable file SHALL return an actionable message, never an error.

#### Scenario: A big file gets a cheap outline
- **GIVEN** a source file with nested types and mixed members
- **WHEN** `cs_outline` runs for it
- **THEN** signatures carry no body text, nesting is visible via container paths, and results are in source order

#### Scenario: Outline works without a solution
- **GIVEN** a backend with no loadable solution
- **WHEN** a file under the root is outlined
- **THEN** the outline is produced (no solution required) while statuses elsewhere stay unchanged

### Requirement: C# rename tool
The optional `cs_rename` tool SHALL compute a solution-wide rename plan for a resolved symbol — per-file, bounded occurrence lists with before/after text and totals, deterministic order — and SHALL NOT modify any file or the workspace: the plan is applied only through the existing edit/approval path. `newName` SHALL be validated as a legal C# identifier; ambiguous names SHALL return candidates; metadata targets SHALL return a message; the tool SHALL be read-only and its text SHALL state that nothing was changed.

#### Scenario: Rename is planned, disk untouched
- **GIVEN** a loaded fixture with a symbol used in two projects
- **WHEN** `cs_rename` plans a rename
- **THEN** the plan lists both files with expected occurrences, and the fixture tree is byte-identical afterwards

#### Scenario: Invalid new names are rejected with guidance
- **GIVEN** a legal target symbol
- **WHEN** `newName` is not a legal C# identifier
- **THEN** the result explains why with no plan produced
