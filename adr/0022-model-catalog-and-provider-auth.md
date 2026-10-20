# ADR-0022: Generated model catalog, provider presets and API-key auth

- Status: accepted — 2026-10-20 (maintainer sign-off for `provider-presets`)
- Date: 2026-10-20
- Relates to: ADR-0002 (MEAI model types), ADR-0010 (Anthropic adapter), ADR-0015 (session format), ADR-0018 (session schema 2), ADR-0021 (per-run workspaces)

## Context

The catalog is four hand-written entries and the factory knows two providers. Every other
OpenAI-compatible endpoint requires a hand-written `models.json` entry, a provider name the factory
does not recognise unless it is spelled `openai`, and a key in the `openai` slot. The catalog also
carries no price, output limit or vision flag, so the footer and `/session` cannot say what a turn
cost. models.dev publishes that metadata across providers, and users routinely switch between
several of them.

The constraints that shape the decision are: no new packages; `Lunate.Ai` remains the only layer
that builds provider clients; no secret may reach a third-party endpoint by accident; and session
files are a pinned format (ADR-0015/0018), so any persisted usage change is a schema event.

## Decision

1. **The catalog is a generated snapshot of models.dev.** `src/Lunate.Ai/models.json` is generated
   for the preset providers and committed. One C# mapper (`ModelsDevCatalog`) turns models.dev
   records into catalog entries; the repo script and the runtime `--update-catalog` command call the
   same mapper. A repo script regenerates the embedded file; `lunate --update-catalog` writes an
   optional managed cache under `~/.lunate/`, and the hand-written `~/.lunate/models.json` keeps
   merging last.
2. **Schema 2 adds pricing and capabilities.** Catalog entries carry `pricing` (input, output,
   cache read, cache write per million tokens), `maxOutput`, `supportsVision` and an optional
   `wireId`. Schema 1 is accepted read-side; the four existing bare ids do not change. Native
   providers keep bare ids; other providers use `<provider>/<model>` and the upstream id in
   `wireId`.
3. **Providers are data, not branches.** One embedded `providers.json` lists each preset's adapter,
   base URL and key variable. `openai` and `anthropic` stay native; the other presets use the
   existing OpenAI-compatible adapter. A preset base URL may receive its environment key; a
   user-declared endpoint keeps the placeholder-credential rule and needs an explicit `auth`
   reference.
4. **Auth is still API keys in `AuthStore`.** `/login <provider>` stores a key through the existing
   owner-only store, `/logout` removes it, and the provider environment variable and a model's
   `auth` reference remain the fallbacks. Subscription OAuth is not added: those plans authorize
   first-party clients and third-party use is outside the providers' terms.
5. **Prices are display-only catalog data.** A turn or session cost is shown only when every token
   lane in it has a known rate; an unknown price shows tokens, never `$0`. The catalog rate is the
   base rate; context-tier pricing is not applied.
6. **Cost detail is persisted additively.** Session usage gains optional cache-read and cache-write
   counts (schema 3, loader accepts 1/2/3), so a resumed session can be costed exactly. Existing
   goldens keep their bytes; a new v3 golden pins the fields.

## Consequences

- Adding a provider becomes a `providers.json` row plus a catalog regeneration, not a factory
  branch. Adding a model never needs a code change.
- The embedded catalog grows to roughly 750 entries; startup parsing is measured by the existing
  performance gate, and the generated file carries a source hash instead of a timestamp so drift is
  reviewable.
- Prices drift with models.dev and context tiers are ignored; the UI treats a missing or
  insufficient rate as unknown, which is correct but makes some turns token-only.
- Session files written by this version are schema 3 and older binaries cannot read them; the
  reverse direction stays valid (schema 1/2 load and round-trip unchanged).
- The model id namespace becomes explicit: bare native ids, qualified non-native ids, and
  `wireId` for the provider call. User files with bare custom ids keep working as bare overrides.
- A stored key is only as safe as the machine's file permissions; `AuthStore` keeps the existing
  owner-only write and the secret-prompt path keeps keys out of history, sessions and logs.
