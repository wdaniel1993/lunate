# Resolve the T-41 review carry: override-log invariant, E2E helper dedup, scenario coverage

## Why

Three non-blocking observations from the T-41 review (card `t_37c40a60`), all about enforcement and coverage rather than behaviour: the "unknown override is logged" invariant is call-site-dependent (the log delegate on `McpServerHost` is optional, so a future caller could silently drop the spec-required warning); the T-41 end-to-end test re-derives the shared `TestMcpServer` path/env helpers (now in three copies across three test projects); and two extension-level spec scenarios (registration-only, duplicate refusal) are covered at registry/loader level, not through the end-to-end fixture path.

## What Changes

- **The invariant gets an enforcement point (no behaviour change).** `McpServerHost`'s public constructor stays as-is: it is pre-ship (`PublicAPI.Unshipped.txt`), so tightening it would be legal, but AGENTS.md's policy is that public types change only when the change says so — and this carry is enforcement + coverage, not an API break. Enforcement moves to the one production construction path: `McpToolSet` (internal) makes `log` required and non-nullable, so `serverLog ?? log` can never be null in production. The public parameter gets a doc comment stating the invariant and the test-only optionality; the existing mapping test (`McpExposureTests.An_override_naming_no_listed_tool_is_logged_and_the_rest_proceeds`) already asserts the warning and stays.
- **`TestMcpServer` resolution lives in one place.** New BCL-only project `tests/Lunate.TestSupport` with a static `TestMcpServer` (repository root, configuration, server DLL path, dotnet host path, marker-file and process helpers). `Lunate.Protocols.Tests` (`McpTestEnvironment`), `Lunate.Coding.Tests` (`McpTestServerSupport`) and `Lunate.Extensibility.Testing.Tests` (`McpFromExtensionEndToEndTests`) delete their copies and consume it. The layering graph gains exactly three edges (declared in `LayeringChecker`).
- **Scenario coverage is made explicit.** Registration-only: the e2e test additionally asserts nothing is logged at registration (the kit's `Log` is already recorded); duplicate refusal: the deliberate split is documented — it stays at registry + loader level (the loader-level test installs two extensions and loads both), because the kit hosts one extension per host and a second fixture for one assertion is disproportionate.

## Impact

- No spec deltas (`skip_specs`) — every requirement already says what it says; this change enforces and pins, it does not change behaviour.
- No public-API change — the ctor is pre-ship (`PublicAPI.Unshipped.txt`), so tightening it would be legal, but the AGENTS.md public-type policy shapes item 1: the carry keeps the surface as-is and enforces at the internal seam; no new packages; one new test-support project + solution entry + three declared layering edges.
- No sign-off needed: the card pre-authorizes "resolved or explicitly documented".
