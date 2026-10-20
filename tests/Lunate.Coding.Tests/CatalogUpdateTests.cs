using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The explicit refresh: a models.dev snapshot (or a local one) maps through the one catalog mapper
/// and replaces exactly one target atomically, without composing a session or calling a model.
/// </summary>
public sealed class CatalogUpdateTests
{
    [Fact]
    public void A_managed_write_reports_a_summary_and_the_cache_loads()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string managed = temp.File("home/.lunate/models.catalog.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog", "--source", snapshot],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: managed)
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string[] lines = Lines(output.ToString());
        Assert.Equal(4, lines.Length);
        Assert.Contains($"wrote {Path.GetFullPath(managed)}", lines);
        Assert.Contains("models: 3", lines);
        Assert.Contains("providers: 3", lines);
        Assert.Contains(
            $"sha256: {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(snapshot)))}",
            lines
        );
        Assert.Equal((byte)'{', File.ReadAllBytes(managed)[0]);

        ModelCatalog catalog = ModelCatalog.Load(managed);
        ModelInfo openAi = catalog.Find("gpt-fixture")!;
        Assert.Equal("openai", openAi.Provider);
        Assert.Equal(128_000, openAi.ContextWindow);
        Assert.Equal(8_192, openAi.MaxOutput);
        Assert.True(openAi.SupportsTools);
        Assert.True(openAi.SupportsVision);
        Assert.Equal("effort", openAi.Reasoning);
        Assert.Equal(new ModelPricing(1.25m, 5m), openAi.Pricing);
        Assert.Equal("groq-fixture", catalog.Find("groq/groq-fixture")!.WireId);
        Assert.Equal("budget", catalog.Find("claude-fixture")!.Reasoning);
    }

    [Fact]
    public void Output_writes_the_snapshot_where_named_and_leaves_the_managed_path_alone()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string outputPath = temp.File("dist/models.json");
        string managed = temp.File("managed/models.catalog.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog", "--output", outputPath, "--source", snapshot],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: managed)
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(managed));
        Assert.Contains($"wrote {Path.GetFullPath(outputPath)}", Lines(output.ToString()));
        Assert.Equal(
            ["models.json"],
            Directory.GetFiles(temp.File("dist")).Select(path => Path.GetFileName(path)!)
        );
    }

    [Fact]
    public void Inline_output_and_source_values_are_accepted_including_a_file_uri()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string outputPath = temp.File("dist/models.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            [
                "--update-catalog",
                $"--output={outputPath}",
                $"--source={new Uri(snapshot).AbsoluteUri}",
            ],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: temp.File("managed/models.catalog.json"))
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void An_unreachable_source_keeps_the_cache()
    {
        using var temp = new TempDirectory();
        string cache = temp.File("home/.lunate/models.catalog.json");
        byte[] original = WriteFile(cache, "old cache");
        using var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog"],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: cache, Handler: handler)
        );

        Assert.Equal(1, exitCode);
        string message = error.ToString();
        Assert.Contains(ModelsDevCatalog.SourceUrl, message, StringComparison.Ordinal);
        Assert.Contains("could not load", message, StringComparison.Ordinal);
        Assert.Contains("unchanged", message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(cache));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void A_request_timeout_names_the_thirty_second_limit()
    {
        using var temp = new TempDirectory();
        string cache = temp.File("home/.lunate/models.catalog.json");
        byte[] original = WriteFile(cache, "old cache");
        using var handler = new ThrowingHandler(new TaskCanceledException("simulated timeout"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog"],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: cache, Handler: handler)
        );

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "the request timed out after 30 seconds",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Equal(original, File.ReadAllBytes(cache));
    }

    [Fact]
    public void A_non_success_response_names_the_status_and_keeps_the_cache()
    {
        using var temp = new TempDirectory();
        string cache = temp.File("home/.lunate/models.catalog.json");
        byte[] original = WriteFile(cache, "old cache");
        using var handler = new FakeHandler(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable
        )
        {
            ReasonPhrase = "Service Unavailable",
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog"],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: cache, Handler: handler)
        );

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "the source answered 503 (Service Unavailable)",
            error.ToString(),
            StringComparison.Ordinal
        );
        Assert.Equal(original, File.ReadAllBytes(cache));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "openai": { "models": {} } }""")]
    public void An_invalid_source_keeps_the_cache(string payload)
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp, payload);
        string cache = temp.File("home/.lunate/models.catalog.json");
        byte[] original = WriteFile(cache, "old cache");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog", "--source", snapshot],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: cache)
        );

        Assert.Equal(1, exitCode);
        Assert.Contains(snapshot, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("invalid", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("unchanged", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(cache));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void A_managed_write_never_rewrites_the_user_file()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string home = temp.File("home/.lunate");
        Directory.CreateDirectory(home);
        string user = Path.Combine(home, "models.json");
        byte[] original = WriteFile(user, """{ "schemaVersion": 1, "models": [] }""");
        string managed = Path.Combine(home, "models.catalog.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog", "--source", snapshot],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: managed)
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(original, File.ReadAllBytes(user));
        Assert.True(File.Exists(managed));
    }

    [Fact]
    public void A_successful_write_leaves_only_the_target()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string directory = temp.File("cache");
        string managed = Path.Combine(directory, "models.catalog.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(
            ["--update-catalog", "--source", snapshot],
            output,
            error,
            new CatalogUpdateSeams(ManagedPath: managed)
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["models.catalog.json"],
            Directory.GetFiles(directory).Select(path => Path.GetFileName(path)!)
        );
    }

    [Fact]
    public void A_write_failure_keeps_the_old_cache()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string directory = temp.File("readonly");
        Directory.CreateDirectory(directory);
        string cache = Path.Combine(directory, "models.catalog.json");
        byte[] original = WriteFile(cache, "old cache");
        UnixFileMode mode = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exitCode = Run(
                ["--update-catalog", "--source", snapshot],
                output,
                error,
                new CatalogUpdateSeams(ManagedPath: cache)
            );

            Assert.Equal(1, exitCode);
            Assert.Contains("could not write", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(cache), error.ToString(), StringComparison.Ordinal);
            Assert.Contains("unchanged", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(cache));
            Assert.Empty(output.ToString());
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }
    }

    [Fact]
    public void The_command_composes_no_session_and_no_model_call()
    {
        using var temp = new TempDirectory();
        string snapshot = WriteSnapshot(temp);
        string home = temp.File("home/.lunate");
        string managed = Path.Combine(home, "models.catalog.json");
        string sessions = temp.File("sessions");
        var factory = new ThrowingChatClientFactory();
        var print = new PrintModeOptions { Factory = factory, SessionDirectory = sessions };
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Cli.Run(
            ["--update-catalog", "--source", snapshot],
            output,
            error,
            TestContext.Current.CancellationToken,
            printOptions: print,
            catalogUpdate: new CatalogUpdateSeams(ManagedPath: managed)
        );

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.False(factory.Invoked);
        Assert.False(Directory.Exists(sessions));
        Assert.False(File.Exists(Path.Combine(home, "models.json")));
    }

    private static int Run(
        string[] args,
        StringWriter output,
        StringWriter error,
        CatalogUpdateSeams seams
    ) => Cli.Run(args, output, error, TestContext.Current.CancellationToken, catalogUpdate: seams);

    private static string[] Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string WriteSnapshot(TempDirectory temp, string? json = null)
    {
        string path = temp.File("models.dev.json");
        File.WriteAllText(path, json ?? FakeSnapshot());
        return path;
    }

    private static byte[] WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    private static string FakeSnapshot()
    {
        var root = new JsonObject();
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            root[preset.Id] = new JsonObject { ["models"] = new JsonObject() };
        }

        AddModel(
            root,
            "openai",
            "gpt-fixture",
            new JsonObject
            {
                ["limit"] = new JsonObject { ["context"] = 128_000, ["output"] = 8_192 },
                ["tool_call"] = true,
                ["attachment"] = true,
                ["reasoning"] = true,
                ["cost"] = new JsonObject { ["input"] = 1.25m, ["output"] = 5m },
            }
        );
        AddModel(
            root,
            "groq",
            "groq-fixture",
            new JsonObject
            {
                ["limit"] = new JsonObject { ["context"] = 32_000 },
                ["tool_call"] = true,
            }
        );
        AddModel(
            root,
            "anthropic",
            "claude-fixture",
            new JsonObject { ["tool_call"] = true, ["reasoning"] = true }
        );
        return root.ToJsonString();
    }

    private static void AddModel(JsonObject root, string provider, string id, JsonObject entry)
    {
        var providerNode = (JsonObject)root[provider]!;
        var models = (JsonObject)providerNode["models"]!;
        models[id] = entry;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(responder(request));
    }

    private sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromException<HttpResponseMessage>(failure);
    }

    private sealed class ThrowingChatClientFactory : IChatClientFactory
    {
        public bool Invoked { get; private set; }

        public IChatClient Create(ModelInfo model) => throw Failure();

        public IChatClient Create(ModelInfo model, string? apiKey) => throw Failure();

        private InvalidOperationException Failure()
        {
            Invoked = true;
            return new InvalidOperationException(
                "the catalog command must not compose a chat client"
            );
        }
    }
}
