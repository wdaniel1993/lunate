namespace Lunate.Ai.Tests;

public sealed class ProviderPresetsTests
{
    private const string Source = "test providers.json";

    [Fact]
    public void All_matches_embedded_provider_rows_in_file_order()
    {
        ProviderPreset[] expected =
        [
            new(
                "openai",
                "OpenAI",
                ProviderAdapter.OpenAi,
                new Uri("https://api.openai.com/v1"),
                "OPENAI_API_KEY"
            ),
            new(
                "anthropic",
                "Anthropic",
                ProviderAdapter.Anthropic,
                new Uri("https://api.anthropic.com/v1"),
                "ANTHROPIC_API_KEY"
            ),
            new(
                "openrouter",
                "OpenRouter",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://openrouter.ai/api/v1"),
                "OPENROUTER_API_KEY"
            ),
            new(
                "deepseek",
                "DeepSeek",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.deepseek.com"),
                "DEEPSEEK_API_KEY"
            ),
            new(
                "moonshotai",
                "Moonshot AI",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.moonshot.ai/v1"),
                "MOONSHOT_API_KEY"
            ),
            new(
                "zai",
                "Z.AI",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.z.ai/api/paas/v4"),
                "ZHIPU_API_KEY"
            ),
            new(
                "opencode",
                "OpenCode",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://opencode.ai/inference/openai/v1"),
                "OPENCODE_API_KEY"
            ),
            new(
                "opencode-go",
                "OpenCode Go",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://opencode.ai/inference/go/openai/v1"),
                "OPENCODE_API_KEY"
            ),
            new(
                "groq",
                "Groq",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.groq.com/openai/v1"),
                "GROQ_API_KEY"
            ),
            new(
                "togetherai",
                "Together AI",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.together.xyz/v1"),
                "TOGETHER_API_KEY"
            ),
            new(
                "xai",
                "xAI",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://api.x.ai/v1"),
                "XAI_API_KEY"
            ),
            new(
                "google",
                "Google",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
                "GOOGLE_API_KEY",
                "GEMINI_API_KEY"
            ),
        ];

        Assert.Equal(expected, ProviderPresets.All);
    }

    [Fact]
    public void Google_preset_exposes_the_gemini_key_variable_alias()
    {
        ProviderPreset google = ProviderPresets.Find("google")!;

        Assert.NotNull(google);
        Assert.Equal("GOOGLE_API_KEY", google.KeyVariable);
        Assert.Equal("GEMINI_API_KEY", google.KeyVariableAlias);
    }

    [Fact]
    public void Find_with_unknown_id_returns_null()
    {
        Assert.Null(ProviderPresets.Find("unknown-provider"));
    }

    [Fact]
    public void Find_with_different_case_returns_null()
    {
        Assert.Null(ProviderPresets.Find("OpenAI"));
    }

    [Fact]
    public void Parse_with_duplicate_id_throws_naming_the_id()
    {
        string json = JsonWithProviders(
            """{ "id": "openai", "name": "OpenAI", "adapter": "openai", "baseUrl": "https://api.openai.com/v1", "keyVariable": "OPENAI_API_KEY" }""",
            """{ "id": "openai", "name": "OpenAI Mirror", "adapter": "openai", "baseUrl": "https://example.test/v1", "keyVariable": "OPENAI_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("openai", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_with_unknown_adapter_throws_naming_the_value()
    {
        string json = JsonWithProviders(
            """{ "id": "openai", "name": "OpenAI", "adapter": "mystery", "baseUrl": "https://api.openai.com/v1", "keyVariable": "OPENAI_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("mystery", exception.Message, StringComparison.Ordinal);
        Assert.Contains("openai", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_with_invalid_base_url_throws_naming_the_value()
    {
        string json = JsonWithProviders(
            """{ "id": "openai", "name": "OpenAI", "adapter": "openai", "baseUrl": "not a url", "keyVariable": "OPENAI_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("not a url", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_with_unsupported_schema_version_throws_naming_the_version()
    {
        const string json = """{ "schemaVersion": 2, "providers": [] }""";

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("2", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_with_missing_name_throws_naming_the_provider()
    {
        string json = JsonWithProviders(
            """{ "id": "openai", "adapter": "openai", "baseUrl": "https://api.openai.com/v1", "keyVariable": "OPENAI_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("openai", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    private static InvalidDataException AssertParseFails(string json) =>
        Assert.Throws<InvalidDataException>(() => ProviderPresets.Parse(json, Source));

    private static string JsonWithProviders(params string[] providers) =>
        $$"""
            {
              "schemaVersion": 1,
              "providers": [
                {{string.Join(",\n            ", providers)}}
              ]
            }
            """;
}
