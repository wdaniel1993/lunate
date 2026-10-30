namespace Lunate.Coding.Tests;

public sealed class ImageCapabilityPreflightTests
{
    [Fact]
    public async Task Known_text_only_profile_refuses_an_image_before_sending()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "text-only", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true,
                  "capabilities": { "inputModalities": ["text"] } }
              ]
            }
            """
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Model = "text-only" },
            modelsPath: modelsPath
        );
        File.WriteAllBytes(host.Temp.File("shot.png"), ImageSamples.Png(2, 3));
        Task run = host.RunAsync();

        host.Console.SendText(host.Temp.File("shot.png"));
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.Frames.Contains("doesn't accept images", StringComparison.Ordinal);
        });

        Assert.Empty(host.Client.Requests);
        Assert.Contains("switch profile or remove the image", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}
