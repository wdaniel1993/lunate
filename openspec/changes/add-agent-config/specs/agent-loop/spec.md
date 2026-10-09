## ADDED Requirements

### Requirement: System prompt and project instructions

The agent SHALL compose its system prompt from an embedded `system-prompt.md` template: identity, the available tools by name, and the working rules (read before edit, verify after change). The composer SHALL append the runtime facts (OS, resolved shell, canonical working directory, UTC date) and the contents of every `AGENTS.md` found from the repository root (worktree root when there is no repository) down to the working directory, in root-to-leaf order, each block introduced by its path. The composed prompt SHALL stay under 1,000 tokens (asserted with a documented heuristic), excluding user AGENTS.md content. The composed prompt SHALL be delivered through the harness system prompt so that it leads the model request as its system message.

#### Scenario: Composed prompt leads the request

- **GIVEN** a harness configured with the composed prompt and a fake model client
- **WHEN** a run starts
- **THEN** the request's first message is the composed system prompt

#### Scenario: AGENTS.md chain is appended root to leaf

- **GIVEN** a repository with `AGENTS.md` at the root and in a nested directory, running in that directory
- **WHEN** the prompt is composed
- **THEN** both files appear, root first, each under its relative path

#### Scenario: Budget holds with an empty chain

- **GIVEN** no `AGENTS.md` files
- **WHEN** the prompt is composed
- **THEN** it measures under 1,000 tokens by the documented estimator
