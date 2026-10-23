# ADR Review Manifest

- Status: completed
- Review date: 2026-10-23

## Review Summary

The command source seam extends the extension contract established by ADR-0017. It adds a public, async, cancellable source interface and one deterministic registry; it does not change the loop, the tool contract, the session format or the process/trust boundary.

## In-Force ADRs Reviewed

- `adr/0017-extensibility-architecture.md` — extensions register capabilities through `IExtensionContext` at creation time; registration starts nothing; in-process extensions are fully trusted; manifest declarations are enforced asymmetrically. The command source follows the same model.
- `adr/0012-tool-contract.md` — the analogous capability seam: one core contract, a manifest declaration, deterministic ordering and contained handler failures. Commands mirror those rules rather than inventing a second dispatch architecture.

## New Durable ADRs Created

- None. The decision is change-local: it applies ADR-0017's existing contract to commands. A future out-of-process command wire or a template source would be the point to reconsider a durable ADR.
