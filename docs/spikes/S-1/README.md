# S-1 — Loop: own vs Microsoft Agent Framework harness

Question: can Lunate's loop be the Microsoft Agent Framework (MAF) harness
instead of our own loop on `IChatClient`? Default stays our own loop unless the
harness passes checks 2–5; borrow parts if it fails some.

## Layout

- `Shared/` — the stub `IChatClient` (canned streams incl. a tool call whose
  arguments are split across chunks), the four stub tools, and helpers shared by
  both agents. Throwaway.
- `OwnLoop/` — minimal own-loop agent on `IChatClient` with `RunStarted`,
  text, tool-call, approval, steering, cancel and `RunFinished` events.
- `MafHarness/` — minimal MAF-harness agent with the same four tools and every
  optional harness feature (todos, planning modes, web search, file memory,
  file access) switched off.
- `run-checks.sh` — builds both, runs the six checks and writes raw evidence to
  `evidence/`.
- `report.md` — the check matrix, evidence pointers and the recommendation.

See `../README.md` for the throwaway rules.
