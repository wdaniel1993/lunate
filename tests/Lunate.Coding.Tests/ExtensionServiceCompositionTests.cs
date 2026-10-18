using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for the service registry: an installed extension's background
/// service starts and stops with the composed session, and one extension consumes another
/// extension's registered service by name — both proven through a real CLI run, not the kit.
/// </summary>
public sealed class ExtensionServiceCompositionTests
{
    [Fact]
    public async Task A_print_run_starts_and_stops_an_installed_extensions_service()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Equal(
            1,
            log.Messages.Count(message =>
                message.Contains("hello service started", StringComparison.Ordinal)
            )
        );
        Assert.Equal(
            1,
            log.Messages.Count(message =>
                message.Contains("hello service stopped", StringComparison.Ordinal)
            )
        );
        // The registry is reachable through composition: core handles and the extension's own
        // service both resolve at factory time.
        Assert.True(
            log.Contains("hello lookups: bus=True, queue=True, service=True"),
            "the composed run did not reach the registry; log:\n" + string.Join("\n", log.Messages)
        );
    }

    [Fact]
    public async Task A_print_run_lets_an_extension_consume_another_extensions_service()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        ServiceConsumerFixture.Install(Path.Combine(store, "extensions", "consumer"));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "go",
            Extensions = new ExtensionSeams { StorePath = store, Log = log },
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        // The consumer resolved the provider's service by name through the shared registry when
        // its run started (after every extension had loaded); the absent-name control proves the
        // lookup is not blindly true.
        Assert.True(
            log.Contains("consumer cross-service lookups: provider=True, absent=False"),
            "cross-extension consumption failed through composition; log:\n"
                + string.Join("\n", log.Messages)
        );
    }
}

/// <summary>The ServiceConsumerExtension fixture copied into this test project's output.</summary>
internal static class ServiceConsumerFixture
{
    internal static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", "ServiceConsumerExtension");

    internal static string Install(
        string targetDirectory,
        string id = "consumer",
        int? slowSessionStartedMs = null
    )
    {
        Directory.CreateDirectory(targetDirectory);
        File.Copy(
            Path.Combine(SourceDirectory, "ServiceConsumerExtension.dll"),
            Path.Combine(targetDirectory, "ServiceConsumerExtension.dll"),
            overwrite: true
        );
        File.WriteAllText(
            Path.Combine(targetDirectory, "extension.json"),
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.5.0","entryAssembly":"ServiceConsumerExtension.dll"}"""
        );
        if (slowSessionStartedMs is int slow)
        {
            string store = Path.GetDirectoryName(Path.GetDirectoryName(targetDirectory))!;
            string settingsDirectory = Path.Combine(store, "extensions-settings");
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(
                Path.Combine(settingsDirectory, id + ".json"),
                System.Text.Json.JsonSerializer.Serialize(new { slowSessionStartedMs = slow })
            );
        }

        return targetDirectory;
    }
}
