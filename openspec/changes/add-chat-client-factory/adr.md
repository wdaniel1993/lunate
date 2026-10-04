# ADR manifest — add-chat-client-factory

Planned at authoring time:

- No new durable ADRs. This change implements decisions already in force:
  - ADR 0002 (layering; Microsoft.Extensions.AI model types) — the factory is the only model-access seam.
  - ADR 0003 (own loop; reuse list) — pipeline order OpenTelemetry → logging → recorder → provider; no `FunctionInvokingChatClient`; no retries in the layer.
- If implementation surfaces a new architectural decision (e.g. a different key seam), stop and propose an ADR instead.

Finalized at archive time.
