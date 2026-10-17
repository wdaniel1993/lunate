# Service-composition production tests

## Problem

The spec-vs-production audit (T-53) left two soft coverage gaps in the extension service layer:

1. `BackgroundServiceHost` is production-wired but only kit/loader-level tested — no test proves a composed **session** starts and stops an installed extension's service.
2. Cross-extension service consumption (`ServiceRegistry` lookups by name) is unit-tested only — no test proves one extension can consume another's registered service through the real composition.

The remaining audit item (four older samples kit-only for their own e2e) is addressed by a written narrowing decision (see `design.md`), not new tests.

## Change

Two production-path tests in `tests/Lunate.Coding.Tests/ExtensionServiceCompositionTests.cs`, both driving a real CLI print run with fixtures installed into a temp store:

- `A_print_run_starts_and_stops_an_installed_extensions_service` — the hello fixture's background service starts exactly once and stops exactly once with the composed session; the run also proves the registry is reachable (core handles + the extension's own service resolve at factory time).
- `A_print_run_lets_an_extension_consume_another_extensions_service` — a new `ServiceConsumerExtension` fixture resolves the hello fixture's registered service by name when its run starts, with an absent-name control proving the lookup is not blindly true.

A new test fixture (`tests/TestExtensions/ServiceConsumerExtension`) is added and wired into the test project like the existing fixtures.

## Notes

- Tests only — no product behaviour changes, hence no spec deltas (`skip_specs`).
- Found while building these tests: `SessionStarted` fires during the first extension's load, so later-loaded extensions' handlers never observe it — filed separately for a maintainer decision (repro on that card); the consumer fixture deliberately consumes at run start (the docs' "on first use" pattern), so the test does not depend on that behaviour.
