namespace Lunate.Ai.Tests;

public sealed class ModelCatalogFailureTests
{
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
                """
            );

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                ModelCatalog.Load(userFile)
            );

            Assert.Contains("gpt-4o-mini", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Load_with_malformed_user_file_throws_naming_the_path()
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(directory, "{");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                ModelCatalog.Load(userFile)
            );

            Assert.Contains(userFile, exception.Message, StringComparison.Ordinal);
            Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 3, "models": [] }""", "3")]
    [InlineData("""{ "models": [] }""", "missing")]
    public void Load_with_unsupported_schema_version_throws_naming_the_version(
        string json,
        string expectedVersion
    )
    {
        string directory = CreateTempDirectory();
        try
        {
            string userFile = WriteUserFile(directory, json);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                ModelCatalog.Load(userFile)
            );

            Assert.Contains(expectedVersion, exception.Message, StringComparison.Ordinal);
            Assert.Contains(userFile, exception.Message, StringComparison.Ordinal);
            Assert.Contains("expected 1 or 2", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"lunate-model-catalog-{Guid.NewGuid():N}"
        );
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
