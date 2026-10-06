## 1. Tool contract

- [x] 1.1 `ToolRisk`, `ITool`, `ToolResult`, `ToolContext` in `Lunate.Agent`; `PublicAPI.Unshipped.txt` updated
- [x] 1.2 Tests: contract shapes (records construct, enum values, `ITool` implementable by a scripted test tool); `Details` defaults to null

## 2. Declaration adapter

- [x] 2.1 `ToolDeclaration : AIFunction` (internal): name, description, schema (raw-text clone); every invocation path throws with ADR-0003 guidance; wrapped tool reachable internally
- [x] 2.2 Tests: schema reaches the model unchanged (raw-text equality, including a schema with unusual key order/formatting); description and name exact; invocation throws `NotSupportedException`

## 3. Registry

- [ ] 3.1 `ToolRegistry`: insertion-ordered; `Add` (duplicate name fails fast), `Find`, `Declarations` (`IReadOnlyList<AIFunction>`), `Tools`
- [ ] 3.2 Tests: add/find; duplicate rejected; declarations match tools in registration order; unknown name returns null

## 4. Output truncation

- [ ] 4.1 `ToolOutput` (`DefaultLimit = 30_000`, `Truncate`): unchanged under the limit; middle cut with marker and omitted count (culture-invariant); surrogate-safe cut points; graceful tiny limits
- [ ] 4.2 Tests: exact boundary (length == limit; limit + 1), head/tail/marker assertions, omitted count, emoji at the cut point, tiny limit, empty string

## 5. Close

- [ ] 5.1 `scripts/verify.sh` green; `openspec validate add-tool-contract --type change --strict`
- [ ] 5.2 Self-review pass; fix findings; commit per group (do NOT spawn subagents; the adversarial review is a separate dispatch)
