using System.Collections;
using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace ModelRouterExtension.Tests;

/// <summary>The counts the sample's registered <c>router-counters</c> service exposes.</summary>
internal sealed record RouterCounterView(int Total, IReadOnlyDictionary<string, int> CallsByModel);

/// <summary>Installs and drives the built sample extensions through the real loader.</summary>
internal static class TestSupport
{
    internal const string Secret = "test-secret";
    internal const string SecretName = "router-key";

    /// <summary>The model-router extension directory the test build copies under the output root.</summary>
    internal static string ExtensionDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "ModelRouterExtension");

    /// <summary>The subagents extension directory, for the two-extension child-run composition.</summary>
    internal static string SubagentsDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "SubagentsExtension");

    internal static string Manifest(string baseUrl) =>
        $$"""
            {
              "id": "model-router",
              "version": "0.1.0",
              "apiVersion": "^1.10.0",
              "entryAssembly": "ModelRouterExtension.dll",
              "tools": ["ask"],
              "settingsSchema": {
                "type": "object",
                "properties": {
                  "deepThresholdChars": { "type": "number", "default": 4000 }
                }
              },
              "modelProviders": [
                {
                  "id": "router-local",
                  "displayName": "Router Local",
                  "endpoint": "{{baseUrl}}",
                  "secretName": "{{SecretName}}",
                  "modelIds": ["fast", "deep"],
                  "contextWindow": 32768
                }
              ]
            }
            """;

    internal const string SubagentsManifest = """
        {
          "id": "subagents",
          "version": "0.1.0",
          "apiVersion": "^1.5.0",
          "entryAssembly": "SubagentsExtension.dll",
          "tools": ["spawn"]
        }
        """;

    /// <summary>
    /// Creates a host over the built sample extension; the manifest's endpoint is rewritten to the
    /// fake endpoint's ephemeral port and the secret is written before the extension loads.
    /// </summary>
    internal static ExtensionTestHost CreateHost(
        TempDirectory temp,
        FakeOpenAiEndpoint endpoint,
        bool withSecret = true,
        string? settings = null,
        Func<AgentHarnessOptions, AgentHarnessOptions>? configure = null
    )
    {
        var host = new ExtensionTestHost(
            new ExtensionTestHostOptions
            {
                TempDirectory = temp.Root,
                ExtensionId = "model-router",
                ExtensionDirectory = ExtensionDirectory,
                SourceFiles = new Dictionary<string, string>
                {
                    ["extension.json"] = Manifest(endpoint.BaseUrl),
                },
                ConfigureHarness = configure,
            }
        );
        WriteSecrets(host.StorePath, withSecret);
        WriteSettings(host.StorePath, "model-router", settings);
        return host;
    }

    internal static void WriteSecrets(string storePath, bool withSecret, string secret = Secret)
    {
        string directory = Path.Combine(storePath, "extensions-secrets");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "model-router.json"),
            withSecret ? $$"""{"{{SecretName}}":"{{secret}}"}""" : "{}"
        );
    }

    internal static void WriteSettings(string storePath, string id, string? settings)
    {
        if (settings is null)
        {
            return;
        }

        string directory = Path.Combine(storePath, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), settings);
    }

    /// <summary>Installs an extension's built output into the store under <paramref name="id"/>.</summary>
    internal static void InstallExtension(
        string storePath,
        string id,
        string sourceDirectory,
        string manifest
    )
    {
        string directory = Path.Combine(storePath, "extensions", id);
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*.dll"))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(directory, "extension.json"), manifest);
    }

    /// <summary>
    /// Reads the sample's registered counter service. The service object lives in the extension's
    /// load context, so the test reads it through its shared interface and reflection.
    /// </summary>
    internal static RouterCounterView Counters(ExtensionTestHost host)
    {
        Assert.True(
            host.Runner.Services.TryGet("router-counters", out IBackgroundService? service),
            "the extension should register the router-counters service"
        );
        Type type = service!.GetType();
        var total = (int)type.GetProperty("Total")!.GetValue(service)!;
        var byModel = new Dictionary<string, int>(StringComparer.Ordinal);
        if (type.GetProperty("CallsByModel")!.GetValue(service) is IEnumerable entries)
        {
            foreach (object? entry in entries)
            {
                Type entryType = entry!.GetType();
                string key = (string)entryType.GetProperty("Key")!.GetValue(entry)!;
                var value = (int)entryType.GetProperty("Value")!.GetValue(entry)!;
                byModel[key] = value;
            }
        }

        return new RouterCounterView(total, byModel);
    }

    /// <summary>Installs both sample extensions (no trust needed: the store is global).</summary>
    internal static async Task<ExtensionLoader> LoadBothExtensionsAsync(
        TempDirectory temp,
        FakeOpenAiEndpoint endpoint,
        CancellationToken ct
    )
    {
        string store = Path.Combine(temp.Root, "store");
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(worktree);
        InstallExtension(store, "model-router", ExtensionDirectory, Manifest(endpoint.BaseUrl));
        InstallExtension(store, "subagents", SubagentsDirectory, SubagentsManifest);
        WriteSecrets(store, withSecret: true);
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                ClientFactory = new ChatClientFactory(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
                ),
            }
        );
        loader.Discover(worktree, "repo");
        var trust = new ScriptedTrustPrompt(true);
        await loader.Load("model-router", worktree, "repo", trust, ct).ConfigureAwait(false);
        await loader.Load("subagents", worktree, "repo", trust, ct).ConfigureAwait(false);
        return loader;
    }

    /// <summary>The extension tools of a loader in registration order, in a runnable registry.</summary>
    internal static ToolRegistry Tools(ExtensionLoader loader)
    {
        var registry = new ToolRegistry();
        foreach (ITool tool in loader.Tools.List())
        {
            registry.Add(tool);
        }

        return registry;
    }

    internal static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
