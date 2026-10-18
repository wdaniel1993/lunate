# Tasks — SessionStarted semantics

- [ ] **Firing point.** Remove the event call from the per-load path (`ExtensionLoader.Load.cs`, `ExtensionLoader.Remote.cs`); make `StartSessionAsync` public with an updated doc comment.
- [ ] **Composition.** Call it once after the load loop in `ExtensionSession.ComposeAsync`.
- [ ] **Regression tests.** Two-extension both-observe (later handler sees the full set); late-load does not observe; existing suites green.
- [ ] **Spec delta.** MODIFIED `Hook wiring` — the `SessionStarted` clause + two scenarios; originals preserved.
- [ ] **Gate + review + PR.**
