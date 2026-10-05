namespace Lunate.Ai.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Load_without_user_file_returns_embedded_starter_models()
    {
        string directory = CreateTempDirectory();
        try
        {
            ModelCatalog catalog = ModelCatalog.Load(Path.Combine(directory, "missing.json"));

            ModelInfo mini = catalog.Find("gpt-4o-mini")!;
            Assert.NotNull(mini);
            Assert.Equal("openai", mini.Provider);
            Assert.Null(mini.Endpoint);
            Assert.Equal(128_000, mini.ContextWindow);
            Assert.True(mini.SupportsTools);

            ModelInfo full = catalog.Find("gpt-4o")!;
            Assert.NotNull(full);
            Assert.Equal("openai", full.Provider);
            Assert.Null(full.Endpoint);
            Assert.Equal(128_000, full.ContextWindow);
            Assert.True(full.SupportsTools);

            ModelInfo sonnet = catalog.Find("claude-sonnet-5-5")!;
            Assert.NotNull(sonnet);
            Assert.Equal("anthropic", sonnet.Provider);
            Assert.Null(sonnet.Endpoint);
            Assert.Equal(1_000_000, sonnet.ContextWindow);
            Assert.True(sonnet.SupportsTools);

            ModelInfo opus = catalog.Find("claude-opus-5-5")!;
            Assert.NotNull(opus);
            Assert.Equal("anthropic", opus.Provider);
            Assert.Null(opus.Endpoint);
            Assert.Equal(1_000_000, opus.ContextWindow);
            Assert.True(opus.SupportsTools);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Load_with_user_override_uses_user_definition_at_embedded_position()
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(
                directory,
                """
                {
                  "schemaVersion": 1,
                  "models": [
                    { "id": "gpt-4o-mini", "provider": "openai", "endpoint": "https://example.test/v1", "contextWindow": 42, "supportsTools": false }
                  ]
                }
                """);

            ModelCatalog catalog = ModelCatalog.Load(userFile);

            ModelInfo model = catalog.Find("gpt-4o-mini")!;
            Assert.NotNull(model);
            Assert.Equal("openai", model.Provider);
            Assert.Equal(new Uri("https://example.test/v1"), model.Endpoint);
            Assert.Equal(42, model.ContextWindow);
            Assert.False(model.SupportsTools);
            Assert.Equal(1, catalog.Models.Count(candidate => candidate.Id == "gpt-4o-mini"));
            Assert.Equal("gpt-4o-mini", catalog.Models[0].Id);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Load_with_new_user_model_appends_it_without_code_changes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(
                directory,
                """
                {
                  "schemaVersion": 1,
                  "models": [
                    { "id": "local-llama", "provider": "openai", "endpoint": "http://localhost:11434/v1", "contextWindow": 8192, "supportsTools": false }
                  ]
                }
                """);

            ModelCatalog catalog = ModelCatalog.Load(userFile);

            ModelInfo model = catalog.Find("local-llama")!;
            Assert.NotNull(model);
            Assert.Equal("openai", model.Provider);
            Assert.Equal(new Uri("http://localhost:11434/v1"), model.Endpoint);
            Assert.Equal(8192, model.ContextWindow);
            Assert.False(model.SupportsTools);
            Assert.Contains(catalog.Models, candidate => candidate.Id == "local-llama");
            Assert.Equal("local-llama", catalog.Models[^1].Id);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Load_with_duplicate_id_in_user_file_throws_naming_the_id()
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(
                directory,
                """
                {
                  "schemaVersion": 1,
                  "models": [
                    { "id": "gpt-4o-mini", "provider": "openai", "endpoint": null, "contextWindow": 1, "supportsTools": true },
                    { "id": "gpt-4o-mini", "provider": "openai", "endpoint": null, "contextWindow": 2, "supportsTools": true }
                  ]
                }
                """);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ModelCatalog.Load(userFile));

            Assert.Contains("gpt-4o-mini", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Load_with_missing_user_file_is_tolerated()
    {
        string directory = CreateTempDirectory();
        try
        {
            ModelCatalog catalog = ModelCatalog.Load(Path.Combine(directory, "does-not-exist.json"));

            Assert.Equal(4, catalog.Models.Count);
            Assert.Contains(catalog.Models, model => model.Id == "gpt-4o-mini");
            Assert.Contains(catalog.Models, model => model.Id == "gpt-4o");
            Assert.Contains(catalog.Models, model => model.Id == "claude-sonnet-5-5");
            Assert.Contains(catalog.Models, model => model.Id == "claude-opus-5-5");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Find_with_unknown_id_returns_null()
    {
        string directory = CreateTempDirectory();
        try
        {
            ModelCatalog catalog = ModelCatalog.Load(Path.Combine(directory, "missing.json"));

            Assert.Null(catalog.Find("unknown-model"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "models": [] }""", "2")]
    [InlineData("""{ "models": [] }""", "missing")]
    public void Load_with_unsupported_schema_version_throws_naming_the_version(string json, string expectedVersion)
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(directory, json);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ModelCatalog.Load(userFile));

            Assert.Contains(expectedVersion, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lunate-model-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WriteUserFile(string directory, string json)
    {
        string path = Path.Combine(directory, "models.json");
        File.WriteAllText(path, json);
        return path;
    }
}
