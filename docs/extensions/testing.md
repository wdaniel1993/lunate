# Testing an extension

The test kit is `Lunate.Extensibility.Testing` (project
`src/Lunate.Extensibility.Testing/Lunate.Extensibility.Testing.csproj`, namespace
`Lunate.Extensibility.Testing`). It hosts one extension through the real loader, the real load
context, the real trust flow and the real harness, with a scripted model on the other side. No
test needs a live API.

## Add the kit

`Lunate.Extensibility.Testing` ships as a preview package (`dotnet add package
Lunate.Extensibility.Testing --version 0.2.0-preview.1`); it is self-contained, so it carries the
harness assemblies an unpublished Lunate package would leave unresolved, and its declared
dependencies are the published ones — the preview pair and the stable contract pair, so the
contract assemblies your tests bind to are the same host-owned copies a real load uses. Inside this
repository the test project references the projects instead, the way the template does (the
packages a feed serves may not exist there yet). The paths below match the template's depth;
adjust them for your layout:

```xml
<ProjectReference Include="../../../src/Lunate.Ai/Lunate.Ai.csproj" />
<ProjectReference Include="../../../src/Lunate.Contracts/Lunate.Contracts.csproj" />
<ProjectReference Include="../../../src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj" />
<ProjectReference Include="../../../src/Lunate.Extensibility.Testing/Lunate.Extensibility.Testing.csproj" />
```

The template's test project also builds the extension and copies its output under
`$(OutDir)/TemplateExtension`, which is what `ExtensionDirectory` points at:

[`TemplateExtension.Tests.csproj`](../../templates/extension/TemplateExtension.Tests/TemplateExtension.Tests.csproj).

## Host one extension

```csharp
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Testing;

string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
await using var host = new ExtensionTestHost(
    new ExtensionTestHostOptions
    {
        TempDirectory = temp,
        ExtensionId = "hello",
        ExtensionDirectory = Path.Combine(AppContext.BaseDirectory, "HelloExtension"),
        Approver = new ScriptedApprover(true),
    }
);
await host.StartAsync(TestContext.Current.CancellationToken);
host.ComposeTools();

ExtensionRunResult result = await host.RunAsync(
    new ReplayChatClient(
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests", "fixtures", "streams", "extension-template-tool-call.jsonl"
        )
    ),
    "call hello__greet",
    TestContext.Current.CancellationToken
);

Assert.Contains(result.Events.OfType<ToolCallResult>(), toolResult => !toolResult.IsError);
```

`TestPaths` is the template's helper; it walks up from the test output directory to `lunate.slnx`.
What the host does:

- copies `ExtensionDirectory` (or writes `SourceFiles`) into
  `<TempDirectory>/worktree/.lunate/extensions/<ExtensionId>`;
- discovers and loads through the real `ExtensionLoader`, with a `ScriptedTrustPrompt` (one allow
  by default; set `TrustDecisions` to queue denials);
- starts the session, so background services start and `SessionStarted` fires;
- runs the real `AgentHarness` against the `IChatClient` you pass in.

All state stays under `TempDirectory`: the store, the trust file, the settings, the session. The
real `~/.lunate` is never touched.

## What the host exposes

| Member | Use |
| --- | --- |
| `Runner` | The real `ExtensionLoader`; inspect descriptors, registries and model resolution. |
| `Tools` | The harness `ToolRegistry`; add probe tools before a run. |
| `ComposeTools()` | Adds the extension's registered tools to `Tools` in production order. Call after `StartAsync`, before a run. |
| `ComposeMcpServersAsync()` | Starts and lists the registered MCP servers and merges their tools; returns `ExtensionMcpServerStatus` values. |
| `Log` | A `RecordingExtensionLog` with `Entries`, `Messages`, `Contains` and `Count(level, contains)`. |
| `Events` | An `EventRecorder` over every run on the host: `Of<T>()`, `Single<T>()`, `FirstIndex<T>()`, `OccurredBefore<TFirst, TSecond>()`. |
| `Trust` | The `ScriptedTrustPrompt`; `Calls`, `Approvals`, `Denials`. |
| `LoadedExtension`, `ExtensionDirectory`, `WorkingDirectory`, `StorePath`, `SessionPath` | The loaded instance and the paths under the temp directory. |
| `RunAsync(client, prompt, ct)` | One prompt in the session; returns `ExtensionRunResult(Events, SessionPath)`. |
| `StopAsync()` | Ends the session: drains file changes, stops services. |
| `UnloadAsync()` | Unloads through the real loader; tools leave the registry and in-flight calls are cancelled. |
| `DisposeAsync()` | Stops, unloads and releases the file-change bus. |

`RunAsync` may be called repeatedly on one host; each call is one prompt in the same session.
`Load`-time failures throw; the session's per-extension isolation belongs to the product, not the
kit.

## Assertions

`SessionAssertions` reads the session file through the real `Session.Load`:

```csharp
SessionAssertions.CountMessages(result.SessionPath, ChatRole.User);
SessionAssertions.CountExtensions(result.SessionPath);
SessionAssertions.CountExtensions(result.SessionPath, "hello");
SessionAssertions.CountNestedCalls(result.SessionPath);
```

Use the event stream for ordering and the session file for what was persisted. `RecordingChatClient`
and `ReplayChatClient` (both in `Lunate.Ai`) record and replay the model side; the template's
`TestPaths` helper locates the checked-in fixtures. If a prompt or tool set changes, re-record the
fixture instead of weakening an assertion.

## Settings, secrets and trust in a test

Write these before the first `StartAsync`/`RunAsync`:

```csharp
string settings = Path.Combine(host.StorePath, "extensions-settings");
Directory.CreateDirectory(settings);
File.WriteAllText(Path.Combine(settings, "hello.json"), """{"mode":"confirm"}""");

string secrets = Path.Combine(host.StorePath, "extensions-secrets");
Directory.CreateDirectory(secrets);
File.WriteAllText(Path.Combine(secrets, "hello.json"), """{"router-key":"test"}""");
```

Trust is scripted: `TrustDecisions = [false]` proves the `trust-denied` path, and a later decision
in the queue answers a later prompt. The project-extension path under the temp worktree is the real
one, so the content hash and the store behave as in a session.

## Remote extensions

`RemoteConformanceRunner` points at any executable that speaks the wire and reports each step:

```csharp
RemoteConformanceReport report = await RemoteConformanceRunner.RunAsync(
    new RemoteConformanceOptions
    {
        FileName = "node",
        Arguments = ["index.js"],
        WorkingDirectory = extensionDirectory,
    },
    cancellationToken
);
Assert.True(report.Succeeded, report.Summary);
```

The manifest and wire reference, including the conformance steps, is
[remote extensions](../extensions-remote.md). The reference implementation is the `node-remote`
sample.

## Run the tests

```bash
dotnet test templates/extension/TemplateExtension.Tests/TemplateExtension.Tests.csproj
dotnet test extensions/permission-gate/PermissionGateExtension.Tests/PermissionGateExtension.Tests.csproj
```

The samples under `samples/extensions/` are the fitness suite: each one proves a core capability
and runs in CI. The supported extensions under `extensions/` match that bar — their tests are
solution members and run in the same gate. Run the extension's own test project before you change it.

## The missing-primitive rule

The spec ends with the rule that governs this section: **a sample that needs a workaround means the
core is missing a primitive** ([`docs/spec/extensibility.md`](../spec/extensibility.md#fitness-suite-samplesextensions-ci)).
Do not hide a missing primitive behind a workaround in a sample or a test. Report it, describe the
primitive the extension needed, and propose the change. The same rule applies to tests: a test that
has to reach around the contract is evidence about the contract.

Every behaviour change has a test. Tests use fixtures under `tests/fixtures/`, never live APIs, and
must not depend on the machine culture or locale.

## See also

- The template's test project: [`TemplateExtensionTests.cs`](../../templates/extension/TemplateExtension.Tests/TemplateExtensionTests.cs)
- The kit's own tests: [`tests/Lunate.Extensibility.Testing.Tests/`](../../tests/Lunate.Extensibility.Testing.Tests/)
- Troubleshooting a failed load: [troubleshooting.md](troubleshooting.md)
