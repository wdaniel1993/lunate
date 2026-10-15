# Tasks — kit-multirun-file-changes

## 1. The primitive

- [x] 1.1 `FileChangeBus.FlushAsync` (public, additive): completes when everything enqueued so far is dispatched; does not begin the drain; `DrainAsync` doc sentence distinguishing the two.
- [x] 1.2 Bus tests: flush observes delivery without ending it; drain after flush still latches (late notify dropped + logged).

## 2. The kit

- [x] 2.1 `ExtensionTestHost.RunAsync` flushes instead of draining; docstring states multi-run + session-end drain.
- [x] 2.2 `StopAsync` drains before `EndSessionAsync` (session-end final flush, production order).
- [x] 2.3 Kit test: two runs on one host deliver both runs' events in order, no drop warning (RED before the fix); existing single-run payload test still green.

## 3. Spec + docs

- [x] 3.1 Delta: extensions spec "File change bus" — flush sentence + scenario; originals verbatim; `openspec validate --all --strict` clean.
- [x] 3.2 `docs/spec/extensibility.md` File change bus section: the flush/drain clause.

## 4. Verify, review, archive

- [x] 4.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [x] 4.2 Adversarial review; fix findings.
- [x] 4.3 Archive; push; PR.
