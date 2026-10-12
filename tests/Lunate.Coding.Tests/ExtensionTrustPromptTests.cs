using System.Globalization;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class ExtensionTrustPromptTests
{
    [Fact]
    public async Task Non_interactive_prompt_refuses_and_logs_an_actionable_message()
    {
        var log = new RecordingExtensionLog();
        var prompt = new NonInteractiveTrustPrompt(log);

        bool approved = await prompt.ApproveAsync(
            new ExtensionTrustRequest(
                Descriptor("hello", "/work/.lunate/extensions/hello"),
                FilesChanged: false
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Contains(
            "project extension 'hello' was not trusted; run an interactive session to approve it.",
            log.Messages
        );
    }

    internal static ExtensionDescriptor Descriptor(string id, string directory) =>
        new(
            id,
            "0.1.0",
            ExtensionScope.Project,
            directory,
            ExtensionManifest.Parse(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"E.dll"}"""
                ),
                Path.Combine(directory, "extension.json")
            )
        );
}
