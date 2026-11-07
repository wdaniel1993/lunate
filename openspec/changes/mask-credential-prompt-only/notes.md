# Notes — mask-credential-prompt-only

## grill-me record (self-administered, unattended run)

The proposal artifact's rules require the grill-me interview. No user is watching this run, so each
branch of the decision tree was resolved from the card and the codebase instead, as the skill
directs ("answered by exploring the codebase"):

1. Which prompts mask? — The card names them: wizard's "API key for …", `/auth`'s key, `/login`'s
   key. Confirmed against code: those three call sites kept `ReadAsync`.
2. What does the visible prompt do with pasted spaces? — Keeps them; the credential strips all
   whitespace. Confirmed in `TuiSecretPrompt`: `_masked ? WithoutWhitespace(key.Text) :
   WithoutNewlines(key.Text)` — a trailing newline is stripped, never Enter, in both modes.
3. Does the scripted test prompt need a visible mode? — No: `ReadTextAsync` defaults to `ReadAsync`,
   so scripted prompts keep their one answer queue and existing suites stay untouched.
4. Does visible plaintext enter the live state? — Yes for visible fields (they are not secrets);
   for a secret, only the asterisk mask is posted. The audit test pins the secret case.
5. Does `/login` or the credential-reference branch change? — No; both keep `ReadAsync`, and the
   audit test covers the reference branch asserting no extra prompt is masked.
6. Session format or public API touched? — No; no golden files, no `PublicAPI.Unshipped.txt` entries
   (the surface is internal).
7. Subagent routing (AGENTS.md senior-dev)? — The src work predates this session (already on the
   branch); no productive subagent call to make, and the implementation is already test-first with
   the audit suite. Recorded here rather than silently skipped.

## Provenance

The implementation commit(s) came from an earlier session on this branch. This session verified the
symptom at the base (origin/main masks every prompt; the audit test fails there by construction),
verified the fix locally (Release build clean; Lunate.Coding.Tests 2619 pass under default and de-AT
culture; csharpier clean), added the OpenSpec artifacts, and reconciled the branch divergence
(the earlier remote head sat on an old main; the same patch re-landed on current main pushed with
`--force-with-lease`).
