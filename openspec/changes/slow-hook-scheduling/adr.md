# ADR review — slow-hook-scheduling

ADR review completed for this change.

**In-force ADR context reviewed:** 0005 budget calibration and its addendum (interference only ever
adds time, and a shared runner inflates the middle of every distribution — the reason this change
judges a *crossing* instead of trusting a single wall time), 0009 memory budgets, 0011 repo quality
tooling, 0017 extensibility architecture and its remote-host addendum (hooks are the extension
contract; `IPreparationTimeReporter` is the existing seam for a handler that can time itself), 0022
model catalog. None of them decides this question.

**No major durable architectural decision was introduced, so no repository-level ADR file was
created.** The rule this change lands is a behaviour contract, and it belongs where it already lives:
the `Timeouts and continuation caps` requirement in `openspec/specs/extensions/spec.md`. It adds no
dependency, no public API, no layer change and no machinery — the witness is two counter reads — and
it is reversible by deleting one branch.
