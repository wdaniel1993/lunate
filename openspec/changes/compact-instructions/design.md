## Context

`CompactNowAsync` calls `ApplyCompactionAsync`, which asks `CompactionReducer` for a reduction. The reducer implements `IChatReducer.ReduceAsync(IReadOnlyList<ChatMessage>, CancellationToken)` and owns the fixed summarization prompt. `IChatReducer` has no parameter for focus text, and the harness should not grow a mutable "pending instructions" field that a concurrent path could read.

## Decisions

**Instructions are a parameter, not state.** Add `ReduceAsync(messages, instructions, ct)` on `CompactionReducer`; the interface implementation delegates with `null`. The harness threads the string from `CompactNowAsync(instructions, ct)` down through `ApplyCompactionAsync`. Nothing is stored between calls, so an automatic compaction can never inherit a user's focus and a second `/compact` without arguments is clean.

**The fixed prompt stays the skeleton.** The instructions are appended as a labelled focus block after the existing sections, so a summary always answers "what was the goal, what was decided, what files, what is open" first, and only then what the user asked to emphasise. Replacing the prompt outright would let a terse instruction produce a summary missing the four things resume depends on.

**The cap is enforced at the command boundary.** More than 1,000 characters is refused with a notice naming the limit rather than truncated, because a silently shortened instruction is a different instruction. The cap lives in the command, not the reducer, so the agent API accepts any string and tests can drive the reducer directly.

**Not persisted.** The `compaction` entry keeps `summary` and `replaces`. Storing focus text would make the session file explain why a summary looks the way it does — tempting — but it would also replay into future compactions on resume, which is the standing-instruction behaviour this design refuses.

## Risks

- **Prompt-injection surface.** These instructions come from the same user who is compacting their own session, so they are not a new trust boundary; they still must not be able to reorder the fixed sections, which is why they are appended rather than substituted.
- **Test or it did not happen.** Two tests carry the contract: the built request contains the focus text and all four fixed sections; and an automatic compaction's request contains none of a previous `/compact` focus.
- **Print mode** has no `/compact`; the agent API is the only entry point there, unchanged when instructions are null.

## Public surface

`CompactNowAsync` gains an optional `string? instructions = null` parameter. `AgentHarness.PublicAPI.Unshipped.txt` records it; existing callers are source-compatible.
