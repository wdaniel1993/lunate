# Tasks — collision-guard-session-proof

## 1. Draft

- [x] 1.1 Proposal + design + tasks; `openspec validate --all --strict`; draft commit.

## 2. Apply

- [x] 2.1 Session-level test `An_extension_tool_beats_a_colliding_mcp_tool_through_session_composition` in `tests/Lunate.Coding.Tests/ExtensionToolCompositionTests.cs` (per D1: single declaration via the system prompt, skip diagnostic naming both sources, extension handler executes).
- [x] 2.2 Scope decision documented in `src/Lunate.Coding/ToolComposition.cs` doc comment (per D2).
- [x] 2.3 Touched suite green (`Lunate.Coding.Tests`, Perf trait excluded).

## 3. Gate

- [ ] 3.1 `scripts/verify.sh` green (both cultures).

## 4. Review

- [ ] 4.1 Adversarial review; fix findings; focused re-review.

## 5. Close

- [ ] 5.1 Archive (`openspec archive collision-guard-session-proof --yes`); tick tasks.
- [ ] 5.2 PR + CI green + closing comment.
