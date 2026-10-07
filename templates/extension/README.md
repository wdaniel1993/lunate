# Extension template

A working starting point for a Lunate extension. Copy it, rename it, replace the handlers:

```sh
cp -r templates/extension my-extension
```

Then rename `TemplateExtension` and the project, update `extension.json`, and keep the test project
as the place where your extension's behavior is proven.

## Layout

```text
templates/extension/
  TemplateExtension/          the extension: factory, hook handler, background service, manifest
  TemplateExtension.Tests/    loads the built extension through the real loader and replays a fixture
  README.md
```

## `extension.json`

The manifest the host reads before loading anything. Every field matters:

| Field | Meaning |
| --- | --- |
| `id` | Lowercase `[a-z0-9-]+`; names the extension and prefixes its service names (`ext/<id>/…`). |
| `version` | Your extension's version. |
| `apiVersion` | The `Lunate.Extensibility.Abstractions` surface you target; `^` ranges track the major version. |
| `entryAssembly` | The file name of the built assembly inside the extension directory; declare exactly one public `IExtensionFactory`. |
| `hooks` | The hook kinds you register, for discovery and documentation. |
| `services` | The service names you register; `ext/<id>/<name>` is the convention. |
| `settingsSchema` | Optional JSON schema for `~/.lunate/extensions-settings/<id>.json`; a `required` entry fails the load when missing, and declared property types are checked. |

## Wiring hooks and services

`TemplateExtensionFactory.Create` is the single entry point. From its `IExtensionContext` you can:

- `context.Register(handler)` — register any `IHookHandler`. The template registers one
  `IToolCallingHandler` that logs and proceeds; register the other hooks you need the same way.
- `context.RegisterService(name, service)` — register a named `IBackgroundService`. The host starts
  it once the session is live and stops it idempotently when the session ends.
- `context.SubscribeFileChanged(handler, pattern)` — observe successful file mutations.
- `context.Settings.TryGet("template.greeting", out value)` — read your namespaced setting.
- `context.Secrets.TryGet("template-api-key", out value)` — read a secret by name; treat an absent
  secret as "not configured" instead of failing, and never log the value.

Registration is registration only: do not start processes, sockets or timers from `Create`.

## Testing with the kit

`TemplateExtension.Tests` uses `Lunate.Extensibility.Testing`. The test installs the built
extension into a temp directory, loads it through the real loader (manifest, load context, trust
prompt), runs the real agent harness against a `ReplayChatClient` fixture, and asserts the hook
fired, the service started and stopped, and the tool call was approved:

```csharp
await using var host = new ExtensionTestHost(
    new ExtensionTestHostOptions
    {
        TempDirectory = temp.Root,
        ExtensionId = "template",
        ExtensionDirectory = Path.Combine(AppContext.BaseDirectory, "TemplateExtension"),
        Approver = new ScriptedApprover(true),
    }
);
host.Tools.Add(new EchoTool());
ExtensionRunResult result = await host.RunAsync(new ReplayChatClient(fixturePath), "call the echo tool");
```

The fixture under `tests/fixtures/streams/` records the model side; if you change the prompt or the
tool set, re-record it (see `Lunate.Ai` fixtures) instead of weakening the assertions.
