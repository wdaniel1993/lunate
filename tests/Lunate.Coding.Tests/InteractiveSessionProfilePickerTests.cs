using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionProfilePickerTests
{
    [Fact]
    public async Task Model_picker_lists_named_profiles()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("models.json"),
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "chat-one", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "chat-two", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true }
              ]
            }
            """
        );
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "chat-primary": { "provider": "primary", "model": "chat-one" },
                "chat-backup": { "provider": "primary", "model": "chat-two" }
              },
              "usages": { "chat": "chat-primary" }
            }
            """
        );
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(host.Temp.File("auth.json")),
            [Provider("primary")]
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/model");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        host.Console.SendText("chat");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "filter: chat");

        Assert.Contains("chat-primary", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("chat-backup", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("primary/chat-one", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("primary/chat-two", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static ProviderEntry Provider(string name)
    {
        bool valid = ProviderEntry.TryCreate(
            name,
            "openai",
            new Uri("https://provider.example.test/v1"),
            $"auth:{name}",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }
}
