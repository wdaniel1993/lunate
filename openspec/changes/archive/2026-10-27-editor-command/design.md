# Design: `/editor` is a second door to the same room

## Context

`InteractiveSession.RunEditorAsync` is the whole editor flow: it resolves the program through `DraftEditor`, writes the draft to a temp file, suspends the console and the live area, runs the child, reads the text back and reconciles it with the draft (the editor wins; a draft that changed meanwhile goes to the stash). `Ctrl+G` reaches it through `RoutedKey.Editor` in the binding catalog. The registry is the other dispatch surface: one descriptor table (`Commands.Descriptors`) feeds `/help`, `/` completion and the built-in source's switch, and `CommandRegistryTests` fails when a descriptor has no case or a case has no descriptor.

The gap is reachability, not behaviour: `Ctrl+G` is `BEL`, terminals and tmux configurations eat it, and a keybindings file that replaces the row takes the capability away on purpose.

## Goals and non-goals

**Goals.** Every user can reach the editor round-trip whatever their terminal does with `BEL`. The command costs one descriptor and one case. The docs say which key it stands in for.

**Non-goals.** A second chord or an alias row in the catalog; seeding the editor from an argument; re-opening the stash in the editor; any change to `DraftEditor`, the suspend seam, the reconciliation or the notices. Keybindings files (P8) and extension-contributed bindings stay out.

## Decisions

### D1 — The command calls the existing flow; it does not reimplement it

The dispatch case calls `RunEditorAsync` — the same method `RoutedKey.Editor` calls — and returns `CommandResult.None`. Everything the key guarantees is therefore guaranteed by the command by construction, not by a parallel implementation that could drift: the resolve order, the "a set `$VISUAL` that cannot launch is reported, never swapped for `$EDITOR`" rule, the CRLF normalisation and single trailing newline, the cursor at the end, the stash on a draft that changed meanwhile, and the temp file deleted on every path.

Alternative rejected: a command that builds its own temp file and child process. It would double the failure surface of the most delicate plumbing in the session for a second entry point.

### D2 — It shares the key's guards, including the absence of a busy refusal

`Ctrl+G` routes in `KeyContext.None`: a running turn does not take it away, and the spec's own scenario exercises the handover over a live frame with a running turn. A command that refused while busy would be the one place where the same flow behaves differently, and the refusal would be wrong on its own terms — the flow suspends the live area and resumes it, so the run is not disturbed. `/editor` therefore runs whenever it is submitted, exactly where the key runs.

What does gate it is what gates every command: a picker, the transcript viewer, the secret prompt, the trust question and the approval panel return before a submitted line can dispatch, so the command cannot interrupt a question. Submitting happens from the prompt only, and bash mode's Enter runs the line as a shell command, so `/editor` is unreachable there — the same as every other slash command.

### D3 — The draft it edits is the prompt as it stands

Submission clears the input line before the command dispatches (`SubmitAsync` sets the input to the empty string), so a bare `/editor` opens an empty buffer and whatever the user saves becomes the draft. That is not a compromise carved out for the command: the editor requirement already says an empty draft opens an empty buffer, and composing a long message from nothing is half the reason the editor exists. The other half — editing text already in the box — has no draft to act on at that moment, because the command line replaced it.

**Why not seed the buffer from an argument.** `/editor <text>` would make the command's argument a second draft-storage surface with its own rules: what the argument does to the busted draft, whether it survives a failed launch, and whether it round-trips through the stash. The reuse claim would end at the first failure path.

**Why not resurrect a draft the command line replaced.** No component remembers it: typing `/` replaces the box, and the history entry is the command. Reconstructing it would need new state and a new rule for which earlier draft wins.

### D4 — One descriptor, so `/help` and completion cannot miss it

`Commands.Descriptors` is the only table: the built-in source maps it to `CommandDescriptor`s, `CompletionRegistry` composes `/` rows from those, and `HelpView` groups them. Adding the entry in the table with `CommandGroup.Session` — beside the other prompt-side commands — gives the completion entry, the help line and the description requirement (`CommandRegistryTests` rejects a missing, multi-line or full-stop-terminated description) in one place.

The description is `open the draft in your external editor`, which names the mechanism and the target in one line and matches the catalog row's wording (`Open the draft in your editor`).

### D5 — The guide names the alternative where the limitation is stated

The `Ctrl+G` row already explains that the program must wait for the file; it now also says `/editor` runs the same round-trip, so a reader whose terminal eats `BEL` finds the way out where the problem is documented instead of having to read the command list. The editor notes below the table gain the same sentence about what the command acts on, so no reader expects `/editor` to edit a line that the command itself has just consumed.

## Risks

- **A reader may expect `/editor` to edit text already typed.** The guide sentence in D5 is the mitigation; the alternative (an argument that seeds the buffer) is refused in D3 for its failure-path cost.
- **Two entry points, one flow.** The risk is a future change that edits the key path and forgets the command; it is bounded by the command holding no logic of its own. A test drives the command end to end over a scripted child so a break in the shared path fails twice.

## Deferred

- **Seeding the buffer** from an argument or the stash slot — a separate decision with its own failure rules.
- **A keybindings file** (P8) — unrelated; this change only makes the capability independent of the chord.
