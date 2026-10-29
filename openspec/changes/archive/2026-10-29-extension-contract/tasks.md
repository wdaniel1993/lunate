# Tasks — extension contract design

## 1. Design artifacts

- [x] 1.1 Record the four decisions — built-ins, per-workspace services, conditional activation
      and stable aliases — in `design.md`, with a `file:line` for every claim about current code.
- [x] 1.2 Add the delta against the `extensions` spec: built-in provenance, per-workspace service
      lifecycle, tool activation and stable aliases, plus the two affected existing requirements.
- [x] 1.3 Append the addendum to `adr/0017-extensibility-architecture.md` covering the four gaps, the
      ordering rule and the banned tool plumbing; accepted 2026-10-29.
- [x] 1.4 Decide structured results: `StructuredContent` plus a content kind is part of the stable
      contract, defined and populated now, aligned with MCP's `structuredContent` and `outputSchema`.
- [x] 1.5 Record the deliberate non-goals: no production code, tests, fixtures or migration; no
      settings move; no built-in reload; no live mid-run activation; no remote workspace services in
      v1.

## 2. Validation

- [x] 2.1 `openspec validate extension-contract --strict` passes.
- [x] 2.2 `openspec validate --all` passes.
- [x] 2.3 `python3 scripts/docs-sync --check` passes.
- [x] 2.4 `npx --yes markdownlint-cli2@0.23.3` passes for the ADR change.
- [x] 2.5 Open the draft PR with `Refs #268`, and keep the body and findings current.

## 3. Sign-off (blocking)

- [x] 3.1 Maintainer sign-off on `design.md` and the ADR-0017 addendum — accepted 2026-10-29,
      including the MCP roots/`list_changed` mechanism and `StructuredContent`.

## 4. Follow-up changes (not part of this change)

- [ ] 4.1 `extension-contract-primitives` (tracked as #292): implement the four additive primitives, test-first, while
      the flagship stays on the host composition. The API version moves semver-minor; the manifest
      fields and context methods land; the built-in path is exercised by a fitness-suite extension.
- [ ] 4.2 `roslyn-built-in-migration` (tracked as #293): move `Lunate.Roslyn` onto the built-in path, the `roslyn`
      workspace service, the `.sln`/`.slnx`/`.csproj` activation rule and the `cs_*` aliases. Prove
      behaviour, names and budgets against the recorded baseline. Update the guide and architecture
      claims in this change, because only now are they true.
- [ ] 4.3 `extension-contract-stable` (tracked as #237): declare the contract stable, close the audit's P1 items and
      publish the semver promise. This is the last step, not the first.
- [x] 4.4 Decide the open question: `ToolResultView` carries `StructuredContent` — decided in the
      addendum (e) on 2026-10-29, defined and populated now, aligned with MCP.
