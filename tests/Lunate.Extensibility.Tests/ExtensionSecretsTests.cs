using Lunate.Agent;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionSecretsTests
{
    [Fact]
    public void Missing_secrets_file_yields_empty_secrets()
    {
        using var temp = new TempDirectory();
        var options = TestExtensions.Options(temp.Subdirectory("store"));

        ExtensionSecretsStore secrets = ExtensionSecretsStore.Load(options, "hello");

        Assert.False(secrets.TryGet("token", out _));
    }

    [Fact]
    public void TryGet_reads_only_string_values()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-secrets"));
        File.WriteAllText(
            Path.Combine(store, "extensions-secrets", "hello.json"),
            """{"token":"s3cret","count":3}"""
        );
        var options = TestExtensions.Options(store);

        ExtensionSecretsStore secrets = ExtensionSecretsStore.Load(options, "hello");

        Assert.True(secrets.TryGet("token", out string? value));
        Assert.Equal("s3cret", value);
        Assert.False(secrets.TryGet("count", out _));
        Assert.False(secrets.TryGet("missing", out _));
    }

    [Fact]
    public void A_secrets_file_that_is_not_an_object_fails_naming_the_extension()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-secrets"));
        File.WriteAllText(Path.Combine(store, "extensions-secrets", "hello.json"), "[1]");
        var options = TestExtensions.Options(store);

        ExtensionLoadException exception = Assert.Throws<ExtensionLoadException>(() =>
            ExtensionSecretsStore.Load(options, "hello")
        );

        Assert.Contains("hello", exception.Message);
    }

    [Fact]
    public async Task Secrets_are_read_by_the_extension_and_never_written_to_a_session()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string directory = Path.Combine(store, "extensions", "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(directory, "hello");
        Directory.CreateDirectory(Path.Combine(store, "extensions-secrets"));
        File.WriteAllText(
            Path.Combine(store, "extensions-secrets", "hello.json"),
            """{"token":"s3cret-value"}"""
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(store, log));
        loader.Discover(temp.Root, "repo");

        await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            log.Messages,
            message => message.Contains("secret present: True", StringComparison.Ordinal)
        );

        string sessionPath = Path.Combine(temp.Subdirectory("sessions"), "s.jsonl");
        Session session = Session.Create(sessionPath, temp.Root);
        session.AppendExtension("ext/hello/note", """{"ok":true}""");

        string sessionText = File.ReadAllText(sessionPath);
        Assert.DoesNotContain("s3cret-value", sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "s3cret-value",
            string.Join("\n", log.Messages),
            StringComparison.Ordinal
        );
    }
}
