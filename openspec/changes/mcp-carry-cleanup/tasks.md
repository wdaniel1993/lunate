# Tasks — mcp-carry-cleanup

## 1. The override-log invariant

- [ ] 1.1 `McpToolSet`: `log` required non-nullable + null-guard; update the 7 test construction sites (5 add an explicit log; keep the 2 that assert). Doc comment on `McpServerHost`'s `log` parameter (invariant + why optional stays). No public-API change.
- [ ] 1.2 Focused: `McpExposureTests` + `McpToolSetTests` + `McpNameCollisionsTests` green.

## 2. Shared TestMcpServer resolution

- [ ] 2.1 New `tests/Lunate.TestSupport` project (BCL-only) with static `TestMcpServer`; add to the solution.
- [ ] 2.2 Consumers deduplicated: `McpTestEnvironment` (Protocols.Tests), `McpTestServerSupport` (Coding.Tests), `McpFromExtensionEndToEndTests` (Extensibility.Testing.Tests) — copies deleted, `TestMcpServer.*` used; project-specific extras stay local.
- [ ] 2.3 `LayeringChecker.AllowedReferences`: the three new edges; `LayeringTests` green.
- [ ] 2.4 Focused: the three projects' MCP suites green.

## 3. Scenario coverage

- [ ] 3.1 E2E test: nothing-logged-at-registration assertion (scoped to the actual run); duplicate-refusal split documented in design.md (D3).
- [ ] 3.2 Focused: `McpFromExtensionEndToEndTests` green.

## 4. Docs + validate

- [ ] 4.1 Project list in docs (if one exists) gains `Lunate.TestSupport`; `openspec validate --all --strict` clean.

## 5. Verify, review, archive

- [ ] 5.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [ ] 5.2 Adversarial review; fix findings.
- [ ] 5.3 Archive; push; PR.
