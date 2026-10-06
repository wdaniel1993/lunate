## 1. The page

- [ ] 1.1 `docs/architecture.md`: intro (what the page is, the one-hop rule) plus four Mermaid diagrams — system map (edges from the real `ProjectReference` graph), model pipeline order, one run (sequence, spans noted), event path — each with a short paragraph and links to its spec/ADR home
- [ ] 1.2 Prose reviewed against sources: `*.csproj`, `ChatClientFactory`, `AgentHarness`, `agent-loop` and `agent-events` specs

## 2. Links

- [ ] 2.1 `docs/README.md`: entry for `architecture.md` in the map
- [ ] 2.2 `docs/guide.md`: the architecture placeholder becomes a pointer to the page
- [ ] 2.3 `README.md`: the documentation list mentions the architecture overview

## 3. Spec

- [ ] 3.1 `repo-quality` delta in this change: "Architecture documentation" requirement (small diagrams, linked homes, updated in the change that alters the structure) with scenarios

## 4. Close

- [ ] 4.1 `scripts/verify.sh` green (docs lint covers the new page); `openspec validate add-architecture-docs --type change --strict`
- [ ] 4.2 Self-review pass; fix findings; commit per group
