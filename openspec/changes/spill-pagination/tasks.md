## 1. Implement spill pagination and exemption

- [ ] 1.1 Make `read` return bounded contiguous pages for known spill files, with an explicit continuation in each result that has more content; enforce the existing `output.spillThresholdTokens` estimate for the complete result.
- [ ] 1.2 Exempt only the exact resolved paths of known session spill files from spill admission, while preserving refusal for paths outside the session's spill files.

## 2. Verify completion

- [ ] 2.1 Add the end-to-end acceptance test that writes an approximately 50,000-token result and reads it to completion through successive `read` calls; assert every byte is reachable, no result is a spill pointer, and the page content joins to the original text exactly.

## 3. Validate the change

- [ ] 3.1 Run `openspec validate spill-pagination --strict` and `openspec validate --all --strict`.
