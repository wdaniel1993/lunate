# Tasks: edit tier 3, start_line and closest-region errors (T-14)

## 1. Matching engine (Coding)

- [x] 1.1 Tier engine: per-tier match collection; zero → next tier; one → apply; several → `start_line` narrowing (|start - start_line| <= 3) → apply or the ambiguity error listing every match; no fall-through
- [x] 1.2 Tier 3 `indent`: uniform non-empty whitespace prefix rule; apply with `new_text` re-indented; blank-line handling per design
- [x] 1.3 Whitespace-significant refusal (`.py` `.yaml` `.yml` `.mk` `Makefile`, case-insensitive) with the pinned error text
- [x] 1.4 Closest-region error: best trimmed-equal window (earliest on ties); pinned format; plain message when nothing resembles `old_text`
- [x] 1.5 Unit tests: tier order, precedence (normalized-none → indent), start_line edges, prefix uniformity, blank lines

## 2. Tool surface (Coding)

- [ ] 2.1 `start_line` argument (optional integer ≥ 1) + description; result string `(match: exact|normalized|indent)`; `EditDetails` unchanged

## 3. Corpus (Coding)

- [ ] 3.1 Runner: optional `start_line` passthrough + optional `file_name` (default `input.txt`)
- [ ] 3.2 New cases per design: `indent-applied`, `indent-normalized-applied`, `indent-refused-python`, `indent-refused-yaml`, `indent-refused-makefile`, `start-line-applies`, `start-line-still-ambiguous`, `start-line-ignored-single-match`, `closest-region-error`, `exact-ambiguity-error` (byte-exact `expected` / `expected-error.txt`)

## 4. TUI (Tui)

- [ ] 4.1 `TierStyle`: `indent` renders as a flagged fallback like `normalized`; renderer test

## 5. Gate

- [ ] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
