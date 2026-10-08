## ADDED Requirements

### Requirement: C# symbol lookup tool
The optional `cs_find_symbol` tool SHALL find definitions for a type, member or namespace name in the loaded solution and return their signature and source location (file, 1-based line and column), without grepping. It SHALL be read-only, SHALL load the C# backend lazily on first use, and SHALL reuse the diagnostics tool's load statuses (`no SDK`, `restore required`, `no solution`, partial load) as actionable messages. Matches SHALL be deterministic (stable ordering), bounded with a truncation flag, and SHALL mark metadata-only symbols (no source location) distinctly. An unknown name SHALL produce an empty result with a short hint, never an error.

#### Scenario: A type is found with its signature and location
- **GIVEN** a loaded fixture solution containing a known type
- **WHEN** `cs_find_symbol` searches for its name
- **THEN** the match carries the file, 1-based line and column, and the pinned signature format

#### Scenario: Overloads and partial types return every definition
- **GIVEN** a fixture with an overloaded method and a partial type
- **WHEN** the method name or partial type name is searched
- **THEN** each definition is listed as a separate match in stable order

#### Scenario: Unknown names are not errors
- **GIVEN** a loaded solution
- **WHEN** a name that does not exist is searched
- **THEN** the result is empty with a hint, and the tool reports success

#### Scenario: Load statuses carry through
- **GIVEN** no solution or a missing SDK
- **WHEN** `cs_find_symbol` runs
- **THEN** the result explains the condition the same way `cs_diagnostics` does
