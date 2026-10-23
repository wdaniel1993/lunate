## ADDED Requirements

### Requirement: The welcome header names the running build

The welcome header's version token SHALL be the display version followed by the first seven characters of the build's commit hash, joined by `+` (`v0.1.0+0c96dc6`), so a user can tell two builds apart without running a second command. A build whose informational version carries no commit metadata SHALL show the display version alone rather than a fabricated or truncated token. The `--version` output SHALL keep the full informational version, and the footer SHALL keep the display version.

#### Scenario: A release build shows its commit

- **WHEN** the assembly's informational version is `0.1.0+0c96dc6e5f1a2b3c`
- **THEN** the header's version token reads `v0.1.0+0c96dc6`

#### Scenario: A build without commit metadata shows no hash

- **WHEN** the informational version is `0.1.0` with no `+` metadata
- **THEN** the header's version token reads `v0.1.0`

#### Scenario: The header form does not change the other version surfaces

- **WHEN** any command prints `--version`
- **THEN** it prints the full informational version, hash and all
