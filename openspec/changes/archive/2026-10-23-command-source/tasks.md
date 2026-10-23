## 1. Contract

- [x] 1.1 Red tests: `CommandDescriptor`/`CommandInvocation`/`CommandResult` round-trip their values; `ICommandSource` describe and invoke are async and cancellable.
- [x] 1.2 Add the command contract to `Lunate.Extensibility.Abstractions`, `IExtensionContext.RegisterCommandSource`, the API version bump and `PublicAPI.Unshipped.txt`.

## 2. Loader

- [x] 2.1 Red tests: a provided and declared command loads; a declared command without a provider is recorded; a provided but undeclared command refuses the load; a built-in collision refuses the load naming both; unload removes the source.
- [x] 2.2 Add the command source registry, reserved-name option, load cross-checks, cache and unload cleanup.

## 3. Registry and built-ins

- [x] 3.1 Red tests: the registry orders by kind then source then descriptor; a duplicate keeps the first; unknown commands are notices; a throwing handler is an error result plus a log entry.
- [x] 3.2 Add `CommandRegistry`, the built-in source (moving the dispatch switch), the skill source and the extension wrapper.
- [x] 3.3 Route dispatch, `/` autocomplete and `/help` through the registry; keep `/skill:<name>` byte-identical.

## 4. Extension commands and the view

- [x] 4.1 Red tests: an installed extension command dispatches through the real session (no model call); a declared-but-not-provided command is not runnable and reads `declared, not provided` in `/extensions`; a throwing extension handler is a notice plus a log entry.
- [x] 4.2 Wire extension sources into session composition and render the declared-not-provided gap in the text and JSON views.

## 5. Documentation and gate

- [x] 5.1 Update `docs/guide.md` (extension and slash-command paragraphs) and add the `Commands` section to `docs/spec/extensibility.md`.
- [x] 5.2 Focused suites: command dispatch, completion registry, extensions view, extension loading, layering.
- [x] 5.3 Fast gate: `dotnet tool restore`, `dotnet csharpier check .`, `openspec validate command-source --strict`, `dotnet build lunate.sln -c Release`.
- [x] 5.4 Commit the change and its implementation.
