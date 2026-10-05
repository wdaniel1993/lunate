# 0010 — Anthropic adapter: official SDK behind IChatClient

- Status: proposed — awaiting maintainer sign-off
- Date: 2026-10-05
- Relates to: ADR 0002 (layering, MEAI types), ADR 0003 (own loop), guide tech stack ("Anthropic via an `IChatClient` implementation (check the official SDK first)")

## Context

T-06 adds the second provider adapter. The guide says to check the official SDK first. Verified on nuget.org (2026-10-05): the `Anthropic` package (12.x) is the official Claude SDK for C# (as of v10+), maintained by Anthropic; the community `Anthropic.SDK` (Grant Hamm) and `tryAGI.Anthropic` are the alternatives.

## Decision

- Use the official `Anthropic` package for transport, pinned exactly.
- Wrap it in our own `IChatClient` implementation in `Lunate.Ai` (internal, constructed only by the factory). No third-party MEAI adapter package.
- Adapter names: `openai` (OpenAI-compatible protocol path) and `anthropic` stay as named by the maintainer.
- SDK-level transport retries: decide at apply time whether to disable them so retry behavior is exclusively the loop's (T-10); record the outcome here.

## Consequences

- Official transport: fewer protocol bugs, no dependency on a community project's release cadence; the mapping to MEAI types is ours to maintain (small, covered by scripted-event tests and recorded fixtures).
- Alternatives rejected: community `Anthropic.SDK` (not official, extra surface we do not need); `tryAGI.Anthropic` (previously occupied the `Anthropic` package id; generated SDK, larger surface).
