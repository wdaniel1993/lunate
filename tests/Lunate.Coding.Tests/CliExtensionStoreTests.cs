namespace Lunate.Coding.Tests;

/// <summary>
/// The startup-budget proof: a poisoned store (an extension whose factory writes a marker file)
/// proves non-session commands never read the store, while a print session with the same store
/// really loads it.
/// </summary>
public sealed class CliExtensionStoreTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("--discover")]
    public void Non_session_commands_never_read_the_extension_store(string command)
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", marker);
        var log = new RecordingExtensionLog();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            [command],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );

        Assert.False(
            File.Exists(marker),
            $"{command} must not read the extension store, but the fixture factory ran"
        );
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task A_print_session_with_the_same_poisoned_store_does_load_it()
    {
        using var temp = new TempDirectory();
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(store, "extensions", "hello"));
        string marker = temp.File("load-marker.txt");
        HelloFixture.WriteSettings(store, "hello", marker);
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions printOptions = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Extensions = null,
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "Hi"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            printOptions,
            extensions: new ExtensionSeams { StorePath = store, Log = log }
        );

        Assert.Equal(0, exitCode);
        Assert.True(
            File.Exists(marker),
            "a print session with the poisoned store must load the extension; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.True(log.Contains("created hello"));
    }
}
