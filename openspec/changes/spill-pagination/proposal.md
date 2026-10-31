## Why

The context-hygiene change lets the model read spilled output, but the current contract returns the full file and sends that result through spill admission again. Large outputs can therefore become pointers to pointers instead of readable content. This change fulfills condition (a) of the accepted ADR-0012 addendum (#349) by making `read` itself paginate and keeping known spill-file reads out of the spill step.

## What Changes

- Define bounded `read` pages with an explicit continuation carried in each result, so the model continues by making another `read` call rather than following a file pointer.
- Bound each page using the same `output.spillThresholdTokens` estimate as result spilling.
- Exempt reads of the session's known `tool-output/` files from spill admission.
- Specify an end-to-end acceptance test that reads an approximately 50,000-token result to completion and proves byte reachability, absence of spill pointers, and exact page concatenation.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `agent-tools`: paginated `read` results for spilled output, including the page bound, continuation, and exemption from re-spilling known spill files.

## Impact

- **Implementation:** `ReadTool` and the tool-result spill admission path; no public API or session-format change is intended.
- **Tests:** the relevant tool and spill tests, including the 50,000-token completion acceptance test.
- **Dependencies:** no new packages.
