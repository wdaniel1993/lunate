# Slice 2 of #474 — provider headers and credential placement

Refs #490 (design accepted, slice 1 merged as 09466aa3).

## What

This slice makes the declared provider headers and the credential actually go out on every wire
path: `ProviderHeadersHandler` (always a `lunate/<version>` user agent, then the provider's
declared headers with template substitution over `{session.id}`, `{run.id}`, `{lunate.version}`),
`ProviderCredentialHandler` (bearer default, `x-api-key`, a named header, or a query parameter;
removes any credential header the SDK added first so the key appears exactly once), the
credential-refusal forms (`{credential}`, `{apiKey}`, `{key}` are refused by name at load), and
`RequestContext(SessionId, RunId)` threaded through `IChatClientFactory.Create` with a per-run
ambient so a child session sends its own id.

## TDD ledger (in flight)

- [x] RED probe: `ProviderWireTests.A_plain_openai_chat_call_carries_the_lunate_user_agent_and_bears_the_credential`
      fails at `a_plain_openai_chat_call` — the wire today carries `User-Agent: OpenAI/2.14.0
      (...) MEAI/10.10.1` and the real key in SDK header form. (Symptom proven, not reproduced
      from stale memory: run against the merged slice 1.)
- [ ] GREEN after the handlers land.
