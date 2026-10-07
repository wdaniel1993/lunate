# Tasks — add-extensibility-architecture

## 1. ADR-0017

- [ ] 1.1 `adr/0017-extensibility-architecture.md` at the root (status proposed): principles, contract assembly, ALC sharing, trust model, hook semantics classes, JSON-serializable boundary, out-of-process constraint, relation to ADR-0003/0012/0014

## 2. Spec

- [ ] 2.1 `docs/spec/extensibility.md`: contract + manifest + lifecycle; hook catalogue table with semantics, order and failure policy; tool model summary (linking `agent-tools`); services; UI by mode; code mode and hosted tools; out-of-process constraints; trust, performance and testing rules; the fitness suite (eight reference extensions)
- [ ] 2.2 `docs/README.md` map row for the new spec

## 3. Guide

- [ ] 3.1 Extensions section: reframed onto `Lunate.Extensibility.Abstractions`, hooks/services summary, pointer to the spec
- [ ] 3.2 Tech stack row: extensions = contract assembly + ALC per extension (ADR-0017)
- [ ] 3.3 Non-goals: subagents/plan mode/memory stay out of the core; "subagent-ready core" stated; fitness suite named
- [ ] 3.4 Cards: T-24 and T-34 replaced by the T-36…T-51 series (new subsection table); T-25 dependency updated to T-36
- [ ] 3.5 Roadmap: fitness suite and "subagent-ready" noted where the extension work is introduced

## 4. Close

- [ ] 4.1 `npx markdownlint-cli2` clean; `bash scripts/verify.sh` green; `openspec validate add-extensibility-architecture --type change --strict`; self-review (no contradictions with ADR-0014/T-25/T-35, links resolve)
