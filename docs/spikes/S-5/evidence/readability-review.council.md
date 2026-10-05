# Council Notes: readability-review.md

## Author Summary

No adversarial-author draft was produced for this artifact because task 2.5 is a
review, not an authored design artifact. The `adversarial-reviewer` subagent was
dispatched directly with the variant sources and the shared test list, and asked
for a five-sentence flow per variant plus a concrete hard-to-follow list.

## Reviewer Challenges

- A's load-bearing fake-clock/FIFO ordering is comment-only.
- A's `Apply(input) && input is Frame` ordering reads confusingly.
- A and B read mutable state across threads with only an implicit drain contract.
- B's `Scan` streams one mutable instance; the pulse applies frames outside the
  subject fold, contradicting the class comment.
- B+ duplicates footer/approval state, re-enters the pipeline from a key handler,
  and renders outside the `Sample` cap via view-model `Changed` subscriptions.
- B+ duplicates the approval shortcut mapping between the state machine and the
  view model.

## Resolutions

- Accepted: clarified A's render condition to `var changed = _state.Apply(input);
  if (changed && input is LiveInput.Frame)` so the apply/render split is explicit.
- Accepted: all other findings are recorded verbatim in the review and used in the
  report's readability section; they are the substance of the B+ verdict.
- Rejected: none. No suggested change was incompatible with the spike's task.
- Deferred: thread-safety annotations, drain-contract docs, and the B+ render-cap
  unification are production T-18/T-19 concerns, out of the spike timebox.

## Remaining Risks

- The review snapshots the code before the one-line A edit; line numbers for that
  file may shift by one. The review was not re-run for that trivial change.
- A single reviewer (one model family) ranked readability; the ranking is
  corroborated by the LoC and flake numbers but is not independently replicated.
