## ADR: custom completion entries precede the built-ins

**Status:** accepted (2026-10-23)

**Context.** The autocomplete shows eight rows in registry order; built-ins fill them, so skills and extension commands are invisible at a bare `/`.

**Decision.** Custom entries sort first, and a capped list spends its last row on a hidden-count indicator instead of silently dropping entries.

**Consequences.** Discoverability is structural rather than documented. The cap is now one row smaller when the list overflows, which is why the indicator exists. No ranking table, no per-source configuration.
