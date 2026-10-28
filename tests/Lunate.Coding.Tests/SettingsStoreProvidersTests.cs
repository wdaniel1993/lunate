using System.Text.Json;

namespace Lunate.Coding.Tests;

public sealed class SettingsStoreProvidersTests
{
    [Fact]
    public void Resolve_reads_the_providers_section()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "providers": {
                "local": {
                  "baseUrl": "http://127.0.0.1:11434/v1",
                  "model": "qwen2.5-coder",
                  "catalogName": "local/qwen2.5-coder"
                }
              }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(path, _ => null);

        CustomProvider provider = Assert.Single(settings.Providers);
        Assert.Equal("local", provider.Id);
        Assert.Equal(new Uri("http://127.0.0.1:11434/v1"), provider.BaseUrl);
        Assert.Equal("qwen2.5-coder", provider.Model);
        Assert.Equal("local/qwen2.5-coder", provider.CatalogName);
    }

    [Fact]
    public void A_missing_catalog_name_defaults_to_id_slash_model()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "providers": {
                "local": {
                  "baseUrl": "https://example.com/v1",
                  "model": "qwen2.5-coder"
                }
              }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(path, _ => null);

        Assert.Equal("local/qwen2.5-coder", Assert.Single(settings.Providers).CatalogName);
    }

    [Fact]
    public void An_invalid_scheme_is_reported_without_echoing_the_url()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "providers": {
                "local": {
                  "baseUrl": "ftp://secret.example/v1",
                  "model": "qwen2.5-coder"
                }
              }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(path, _ => null)
        );

        Assert.Contains("providers", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ftp", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.example", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_model_is_reported_without_echoing_the_url()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "providers": {
                "local": {
                  "baseUrl": "https://example.com/v1"
                }
              }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(path, _ => null)
        );

        Assert.Contains("model id", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_provider_field_is_reported_by_name()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "providers": {
                "local": {
                  "baseUrl": "https://example.com/v1",
                  "model": "qwen2.5-coder",
                  "token": "refused"
                }
              }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(path, _ => null)
        );

        Assert.Contains(
            "unknown key 'providers.local.token'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WriteProvider_preserves_unrelated_keys_and_schema_2()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "model": "gpt-4o-mini",
              "approval": "auto-edit",
              "providers": {
                "old": {
                  "baseUrl": "https://old.example/v1",
                  "model": "old-model",
                  "catalogName": "old/old-model"
                }
              }
            }
            """
        );
        var provider = new CustomProvider(
            "local",
            new Uri("http://127.0.0.1:11434/v1"),
            "qwen2.5-coder",
            "local/qwen2.5-coder"
        );

        SettingsStore.WriteProvider(provider, path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal("auto-edit", root.GetProperty("approval").GetString());
        JsonElement providers = root.GetProperty("providers");
        Assert.Equal(
            "https://old.example/v1",
            providers.GetProperty("old").GetProperty("baseUrl").GetString()
        );
        JsonElement entry = providers.GetProperty("local");
        Assert.Equal("http://127.0.0.1:11434/v1", entry.GetProperty("baseUrl").GetString());
        Assert.Equal("qwen2.5-coder", entry.GetProperty("model").GetString());
        Assert.Equal("local/qwen2.5-coder", entry.GetProperty("catalogName").GetString());
    }

    [Fact]
    public void WriteProvider_leaves_no_temp_file_and_resolves_back()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        var provider = new CustomProvider(
            "local",
            new Uri("https://example.com/v1"),
            "qwen2.5-coder",
            "local/qwen2.5-coder"
        );

        SettingsStore.WriteProvider(provider, path);

        Assert.Equal([path], Directory.GetFiles(temp.Root));
        AgentSettings settings = SettingsStore.Resolve(path, _ => null);
        CustomProvider stored = Assert.Single(settings.Providers);
        Assert.Equal(provider, stored);
    }
}
