# Tasks — prompt-caching

Design-first change: each task is one commit and lands on `change/prompt-caching` after the draft is reviewed. No task changes the session format or adds a package.

## 1. Anthropic marker client and wire tests

- [x] 1.1 Add `src/Lunate.Ai/AnthropicPromptCache.cs`: an internal `DelegatingChatClient` that clones the request, marks the last system block, the last tool declaration and the last content block of the latest message with `CacheControlEphemeral`, asserts the marker count is ≤ 4, and leaves history/tool declarations untouched.
- [x] 1.2 Wire it in `ChatClientFactory` for `provider == "anthropic"` only, below `AnthropicTurnMerge` and above the recorder/provider; leave the OpenAI pipeline unchanged.
- [x] 1.3 `tests/Lunate.Ai.Tests/AnthropicPromptCacheTests.cs`: real adapter + capturing handler — three markers on the wire, rolling marker across two turns with identical system/tool bytes, max-four bound with several blocks/tools, no markers on OpenAI.
- [x] 1.4 `dotnet csharpier format .`; commit `feat(ai): mark anthropic prompt-cache breakpoints`.

## 2. Prefix stability

- [x] 2.1 `tests/Lunate.Agent.Tests/AgentPrefixStabilityTests.cs`: a scripted client records two consecutive runs of one harness; assert byte-identical system message and tool declarations, and that a harness built with a fixed `LUNATE_DATE` keeps that date on turn two.
- [x] 2.2 Document the extension exception in the test: a `RunStarting` section edit is a deliberate new prefix; core sections are the guarantee.
- [x] 2.3 Commit `test(agent): pin the cache-stable system prefix`.

## 3. Cache usage accounting and `/context`

- [x] 3.1 `InteractiveSession.Events.AddUsage`: accumulate cache reads from `UsageDetails.CachedInputTokenCount` and writes from `AdditionalCounts["CacheCreationInputTokens"]`; do not add them to `_inputTokens`/`_outputTokens`.
- [x] 3.2 `InteractiveSession.Commands.ContextText`: append `cache: read <n>, write <m>` via `StatusFooterRenderer.FormatTokens`, omitting unknown counts and the whole segment when neither is known.
- [x] 3.3 `tests/Lunate.Coding.Tests/InteractiveSessionCommandTests.cs`: read-only, write-only, both and neither; assert the budget total is unchanged.
- [x] 3.4 Commit `feat(coding): show prompt-cache usage in /context`.

## 4. Cache usage in `--json`

- [x] 4.1 `PrintEventJson.UsageBody`: add `cacheReadTokens` and `cacheWriteTokens` only when the provider reported them; keep the nested `usage` object unchanged.
- [x] 4.2 `tests/Lunate.Coding.Tests/PrintEventJsonTests.cs`: both fields present, each absent when unknown, default (non-JSON) stdout unchanged.
- [x] 4.3 Commit `feat(coding): include prompt-cache usage in json output`.

## 5. Opt-in live smoke

- [ ] 5.1 `tests/Lunate.Ai.Tests/LiveContractTests.cs`: `LUNATE_LIVE=1` + `ANTHROPIC_API_KEY`; a two-turn request with a cacheable system prompt, asserting turn two's `CachedInputTokenCount > 0`; skipped without the flag/key.
- [ ] 5.2 Commit `test(ai): add opt-in prompt-cache live smoke`.

## 6. ADR, docs and the gate

- [ ] 6.1 Land `adr/0010-addendum-prompt-caching.md` (from `adr.md`); note that ADR-0017 is unchanged because the event shape stays the same.
- [ ] 6.2 Update the provider section of `docs/guide.md` with the three breakpoints and the cache usage fields; no new docs page.
- [ ] 6.3 `scripts/verify.sh` green (build, tests, publish, budgets, PublicAPI, CSharpier, docs lint); `openspec validate prompt-caching --strict` green.
- [ ] 6.4 Commit `docs(adr): record prompt caching on the anthropic adapter`.
