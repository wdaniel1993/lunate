# Extension authoring

An in-process extension is a .NET assembly plus an `extension.json` manifest, or a single `.cs` file
whose header declares what it provides ([single-file.md](single-file.md)). It registers
contributions through `IExtensionContext` — tools, hook handlers, services, file-change
subscriptions, MCP servers, model providers, middleware, virtual models, skill sources and terminal
UI pieces — and compiles against `Lunate.Extensibility.Abstractions` and the core tool types it
carries, never against `Lunate.Agent` internals. An out-of-process extension replaces the assembly
with a `runtime` child process; see [remote extensions](../extensions-remote.md).

This section is the authoring guide. The normative contract is
[`docs/spec/extensibility.md`](../spec/extensibility.md); when this guide and the spec disagree,
the spec wins.

| Page | Contents |
| --- | --- |
| [manifest.md](manifest.md) | Every `extension.json` field, what is enforced, and the refusal messages |
| [single-file.md](single-file.md) | One `.cs` file as an extension: the header grammar, the directive table, trust and the `/extensions` states |
| [lifecycle-and-trust.md](lifecycle-and-trust.md) | Global and project scope, loading, content-hash trust, unload, `apiVersion` |
| [contributions/](contributions/README.md) | One page per surface: API, constraints, a real example |
| [testing.md](testing.md) | The test kit, fixtures, and the missing-primitive rule |
| [troubleshooting.md](troubleshooting.md) | `/extensions` states and the failures you can hit |
| [Remote extensions](../extensions-remote.md) | Out-of-process manifest, wire and conformance runner |
| [Contract and hook catalogue](../spec/extensibility.md) | The normative behaviour |
| [Extension template](../../templates/extension/README.md) | A working starting project |
| [Reference extensions](../../samples/extensions/) | The fitness suite that runs in CI |

## Quickstart

### 1. Create the project

The fastest start is to copy [`templates/extension/`](../../templates/extension/README.md) and
rename its projects and namespaces. A new class library needs only the contract project reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>HelloExtension</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
        <ProjectReference
            Include="../../src/Lunate.Extensibility.Abstractions/Lunate.Extensibility.Abstractions.csproj"
            Private="false"
            ExcludeAssets="runtime"
        />
        <ProjectReference Include="../../src/Lunate.Agent/Lunate.Agent.csproj" Private="false" ExcludeAssets="runtime" />
        <ProjectReference Include="../../src/Lunate.Ai/Lunate.Ai.csproj" Private="false" ExcludeAssets="runtime" />
    </ItemGroup>
    <ItemGroup>
        <None Update="extension.json" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
</Project>
```

The contract is not published as a NuGet package in this version; reference the project (or a
local build of it). The path above assumes the extension lives two directories below the Lunate
repository root.

The host owns `Lunate.Extensibility.Abstractions`, `Lunate.Agent` and `Lunate.Ai`, and loads them
once, so every extension binds to those copies. `Private="false"` keeps them out of your output
directory and `ExcludeAssets="runtime"` keeps them out of the deps file, while both still compile
against. A copy in the output is ignored at load, with one line saying which copy and who shipped
it — so keep these references as they are. `PrivateAssets="all"` is not a substitute: it also hides
the `Lunate.Agent` types your signatures use.

### 2. Write the extension

One public `IExtensionFactory` with a public parameterless constructor is the entry point. Its
`Create` method registers contributions and returns the extension instance. This example registers
one read-only tool; the model-facing name is `hello__greet`.

```csharp
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace HelloExtension;

public sealed class HelloExtension : IExtension { }

public sealed class HelloExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        context.RegisterTool(new ExtensionToolRegistration
        {
            Name = "greet",
            Description = "Returns a fixed greeting.",
            ParametersSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
            Annotations = new ToolAnnotations(ReadOnly: true),
            Handler = new GreetHandler(),
        });
        return new HelloExtension();
    }
}

internal sealed class GreetHandler : IExtensionToolHandler
{
    public Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    ) => Task.FromResult(new ToolResult("hello from HelloExtension", IsError: false));
}
```

Registration is registration only: `Create` must not start processes, sockets or timers. Long-lived
resources belong in a registered background service or start on first use — see
[background-services.md](contributions/background-services.md).

### 3. Write `extension.json`

```json
{
    "id": "hello",
    "version": "0.1.0",
    "apiVersion": "^1.10.0",
    "entryAssembly": "HelloExtension.dll",
    "tools": ["greet"]
}
```

`tools` must list every tool the code registers by its local name. The full field reference is
[manifest.md](manifest.md).

### 4. Build and install

```bash
dotnet build -c Release
mkdir -p ~/.lunate/extensions/hello
cp bin/Release/net10.0/HelloExtension.dll \
   bin/Release/net10.0/extension.json \
   ~/.lunate/extensions/hello/
```

Copy any further dependencies next to the assembly. `Lunate.Extensibility.Abstractions`,
`Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` are shared from the
host and are never loaded from the extension folder: a copy shipped there anyway is ignored, and
`/extensions` and the log say so once, naming what was dropped.

There are two installation scopes:

| Scope | Directory | Trust |
| --- | --- | --- |
| Global | `~/.lunate/extensions/<directory>/` | Loaded as-is; no prompt |
| Project | `<worktree>/.lunate/extensions/<directory>/` | Repository code; one trust decision per repository, re-asked when the files change |

The directory name is not the extension identity: `id` in the manifest is. Every directory under
either scope needs its own `extension.json`.

Per-extension settings go in `~/.lunate/extensions-settings/<id>.json` and secrets in
`~/.lunate/extensions-secrets/<id>.json` — see
[settings-and-secrets.md](contributions/settings-and-secrets.md).

### 5. Confirm it loaded

Start `lunate` in a session and run `/extensions`. A loaded global extension reads:

```text
extensions: hello 0.1.0 (global) loaded in 4 ms, tools 1, commands 0, hooks 0, services 0
```

The same data is available without a session as `lunate extensions --json`. Ask the model to call
`hello__greet`; a read-only tool runs without the write approval.

If the extension is missing from the list, it did not survive discovery; the reason is in the
session log (the manifest is read before any code loads). If it reads `trust-denied` or `failed`,
see [troubleshooting.md](troubleshooting.md).

## What to read next

- The contribution you want to add: [contributions/](contributions/README.md).
- When it loads, and how project trust behaves: [lifecycle-and-trust.md](lifecycle-and-trust.md).
- How to test it without live APIs: [testing.md](testing.md).
- The normative semantics, hook catalogue and fitness suite: [`docs/spec/extensibility.md`](../spec/extensibility.md).
