# Upstream request draft — public deterministic test driver

Not filed yet; the maintainer files it.

**Target:** [XenoAtom/XenoAtom.Terminal.UI](https://github.com/XenoAtom/XenoAtom.Terminal.UI) issues

**Title:** Public deterministic test driver: expose the app tick and headless frame capture

**Body:**

Hi! We evaluated XenoAtom.Terminal.UI 3.10.0 for a scrollback-first CLI
(inline mode: `Terminal.Write` for finished output, `Terminal.Live` for a
small live region, `PromptEditor` for input). The architecture fit was good
and the public `InMemoryTerminalBackend` was a pleasure for input injection
and output capture.

The one decisive blocker for us: **deterministic tests** — a virtual clock
and byte-stable frame/golden capture — are only reachable through internals.
Our test suite drives `TerminalApp.Tick` and the golden-screen helpers via
reflection today (spike harness: [S-6 report](../report.md)), which is
version-fragile in a fast-moving library.

Request: a public, documented deterministic test surface, for example:

- a public virtual clock / tick driver (`TerminalApp.Tick` or equivalent)
  usable without reflection;
- a frame-capture helper that renders the current buffer to a stable text
  form (for golden tests in CI);
- optionally, an officially supported way to isolate a `TerminalInstance`
  per test (today a second session disposes the first).

With that, the library would be CI-testable to the standard we require, and
we would re-run our evaluation (its other findings were supporting, not
decisive). Happy to share the spike harness as a reproduction if useful.
