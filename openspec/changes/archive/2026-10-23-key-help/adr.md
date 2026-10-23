# ADR — Generate key help from the binding catalog

Status: accepted (this change).

## Context

Two surfaces already needed to agree about bindings: the router (what a key does) and the guide's
table (what the user is told). They were hand-kept separately until the catalog change (#124) made
the router read one list. The in-app overlay is the third consumer, and the one most likely to
drift because it is generated at runtime from whatever the build ships.

Alternatives considered:

1. **A static help table in the products** (`lazygit`-style, hand-written). Simple, but it is a third
   copy of the same facts; the drift is exactly the bug we just removed.
2. **A `?` cheatsheet in the docs only.** No runtime cost, but it is what the user cannot reach
   while stuck, and it cannot know which context is active.
3. **Generate from `KeyBindingCatalog`.** One source of truth; the overlay is correct by
   construction, and a binding added without a description fails the catalog's own test.

## Decision

The overlay renders `KeyBindingCatalog.All` through `KeyHelpModel`. The catalog row's description
is the row's action text, and `Describe` in the same file is the only place a `KeyPattern` becomes
human-readable. Adding a binding without a description is a test failure, not a silent gap.

The overlay is **read-only** and context-scoped: it answers "what can I press here?" first, and
`Tab` widens to every binding. It never mutates session state, and it consumes only its own keys.

## Consequences

- A binding's help text lives beside its routing: one edit, not three.
- The overlay cannot list an unimplemented binding, and cannot miss one.
- Approval and trust bindings appear under `All` even though the overlay cannot open there; they
  are documentation.
- The guide's table remains prose for readers; the drift check between it and the catalog is a
  separate card, deliberately not smuggled in here.
