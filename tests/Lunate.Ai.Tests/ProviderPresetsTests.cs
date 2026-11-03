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
                "ANTHROPIC_API_KEY",
                BaseUrls: new Dictionary<ProviderApiFamily, Uri>
                {
                    [ProviderApiFamily.AnthropicMessages] = new("https://api.anthropic.com/v1"),
                },
                CredentialPlacements: EveryFamily(CredentialPlacement.XApiKey)
            ),
            new(
                "openrouter",
                "OpenRouter",
                ProviderAdapter.OpenAiCompatible,
                new Uri("https://openrouter.ai/api/v1"),
                "OPENROUTER_API_KEY",
                Headers: new Dictionary<string, string>
                {
                    ["HTTP-Referer"] = "https://github.com/wdaniel1993/lunate",
                    ["X-Title"] = "Lunate",
                }
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
                "OPENCODE_API_KEY",
                BaseUrls: new Dictionary<ProviderApiFamily, Uri>
                {
                    [ProviderApiFamily.OpenAiChat] = new(
                        "https://opencode.ai/inference/go/openai/v1"
                    ),
                    [ProviderApiFamily.OpenAiResponses] = new(
                        "https://opencode.ai/inference/go/openai/v1"
                    ),
                    [ProviderApiFamily.AnthropicMessages] = new(
                        "https://opencode.ai/inference/go/anthropic/v1"
                    ),
                    [ProviderApiFamily.Google] = new(
                        "https://opencode.ai/inference/go/google/v1beta"
                    ),
                },
                ModelsUrl: new Uri("https://opencode.ai/inference/go/v1/models"),
                Headers: new Dictionary<string, string> { ["x-opencode-session"] = "{session.id}" }
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
                "GEMINI_API_KEY",
                BaseUrls: new Dictionary<ProviderApiFamily, Uri>
                {
                    [ProviderApiFamily.OpenAiChat] = new(
                        "https://generativelanguage.googleapis.com/v1beta/openai"
                    ),
                    [ProviderApiFamily.Google] = new(
                        "https://generativelanguage.googleapis.com/v1beta"
                    ),
                },
                CredentialPlacements: new Dictionary<ProviderApiFamily, CredentialPlacement>
                {
                    // Only the native family declares a placement; the OpenAI-compatible surface
                    // falls back to the default bearer in PlacementFor.
                    [ProviderApiFamily.Google] = new("key", CredentialPlacementTarget.Query),
                }
            ),
        ];

        // ProviderPreset is a record, but its dictionary members compare by reference, so the
        // embedded table is compared structurally.
        Assert.Equivalent(expected, ProviderPresets.All);
    }

    /// <summary>One placement declared provider-wide parses to every family the preset serves.</summary>
    private static Dictionary<ProviderApiFamily, CredentialPlacement> EveryFamily(
        CredentialPlacement placement
    ) => ProviderApiFamilies.All.ToDictionary(family => family, _ => placement);

    [Fact]
    public void Google_preset_exposes_the_gemini_key_variable_alias()
    {
        ProviderPreset google = ProviderPresets.Find("google")!;

        Assert.NotNull(google);
        Assert.Equal("GOOGLE_API_KEY", google.KeyVariable);
        Assert.Equal("GEMINI_API_KEY", google.KeyVariableAlias);
    }

    [Fact]
    public void Opencode_go_declares_all_four_families_and_the_discovery_url()
    {
        ProviderPreset preset = ProviderPresets.Find("opencode-go")!;

        Assert.Equal(
            [
                ProviderApiFamily.OpenAiChat,
                ProviderApiFamily.OpenAiResponses,
                ProviderApiFamily.AnthropicMessages,
                ProviderApiFamily.Google,
            ],
            preset.Families
        );
        Assert.Equal(
            new Uri("https://opencode.ai/inference/go/openai/v1"),
            preset.UrlFor(ProviderApiFamily.OpenAiResponses)
        );
        Assert.Equal(
            new Uri("https://opencode.ai/inference/go/anthropic/v1"),
            preset.UrlFor(ProviderApiFamily.AnthropicMessages)
        );
        Assert.Equal(
            new Uri("https://opencode.ai/inference/go/google/v1beta"),
            preset.UrlFor(ProviderApiFamily.Google)
        );
        Assert.Equal(new Uri("https://opencode.ai/inference/go/v1/models"), preset.ModelsUrl);
    }

    [Fact]
    public void A_single_family_preset_resolves_its_default_only()
    {
        ProviderPreset anthropic = ProviderPresets.Find("anthropic")!;

        Assert.Equal([ProviderApiFamily.AnthropicMessages], anthropic.Families);
        Assert.Equal(anthropic.BaseUrl, anthropic.UrlFor(ProviderApiFamily.AnthropicMessages));
        Assert.Null(anthropic.UrlFor(ProviderApiFamily.OpenAiChat));
        Assert.Equal(ProviderApiFamily.AnthropicMessages, anthropic.DefaultFamily);
    }

    [Fact]
    public void Find_with_unknown_id_returns_null()
    {
        Assert.Null(ProviderPresets.Find("unknown-provider"));
    }

    [Fact]
    public void Find_matches_a_different_case_and_keeps_the_canonical_id()
    {
        ProviderPreset preset = ProviderPresets.Find("OpenAI")!;

        Assert.NotNull(preset);
        Assert.Equal("openai", preset.Id);
    }

    [Fact]
    public void Parse_with_malformed_json_throws_naming_the_source()
    {
        InvalidDataException exception = AssertParseFails("{ not json");

        Assert.Contains("malformed", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_with_a_non_object_root_throws_naming_the_source()
    {
        InvalidDataException exception = AssertParseFails("[]");

        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
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
        const string json = """{ "schemaVersion": 3, "providers": [] }""";

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("3", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Source, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_accepts_the_version_1_shorthand()
    {
        IReadOnlyList<ProviderPreset> presets = ProviderPresets.Parse(
            """{ "schemaVersion": 1, "providers": [ { "id": "legacy", "name": "Legacy", "adapter": "openai-compatible", "baseUrl": "https://legacy.example.test/v1", "keyVariable": "LEGACY_API_KEY" } ] }""",
            Source
        );

        ProviderPreset legacy = Assert.Single(presets);
        Assert.Equal("https://legacy.example.test/v1", legacy.BaseUrl.AbsoluteUri);
        Assert.Null(legacy.BaseUrls);
        Assert.Equal(ProviderApiFamily.OpenAiChat, legacy.DefaultFamily);
    }

    [Fact]
    public void Parse_refuses_an_unknown_family_name_in_base_urls()
    {
        string json = JsonWithProviders(
            """{ "id": "sample", "name": "Sample", "adapter": "openai-compatible", "baseUrls": { "openai-chat": "https://sample.example.test/v1", "mistery": "https://sample.example.test/mystery" }, "keyVariable": "SAMPLE_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("mistery", exception.Message, StringComparison.Ordinal);
        Assert.Contains("supported families", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'mistery' in 'baseUrls'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_takes_the_shorthand_url_from_the_default_family_when_only_base_urls_are_declared()
    {
        ProviderPreset preset = ProviderPresets
            .Parse(
                JsonWithProviders(
                    """{ "id": "sample", "name": "Sample", "adapter": "openai-compatible", "baseUrls": { "openai-chat": "https://sample.example.test/v1" }, "keyVariable": "SAMPLE_API_KEY" }"""
                ),
                Source
            )
            .Single();

        Assert.Equal("https://sample.example.test/v1", preset.BaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_refuses_base_urls_without_the_adapters_default_family_and_no_shorthand()
    {
        string json = JsonWithProviders(
            """{ "id": "sample", "name": "Sample", "adapter": "openai-compatible", "baseUrls": { "google": "https://sample.example.test/google" }, "keyVariable": "SAMPLE_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("openai-chat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_keeps_the_declared_models_url_and_headers()
    {
        ProviderPreset preset = ProviderPresets
            .Parse(
                JsonWithProviders(
                    """{ "id": "sample", "name": "Sample", "adapter": "openai-compatible", "baseUrl": "https://sample.example.test/v1", "modelsUrl": "https://sample.example.test/v1/models", "headers": { "X-Known": "literal-value" }, "keyVariable": "SAMPLE_API_KEY" }"""
                ),
                Source
            )
            .Single();

        Assert.Equal("https://sample.example.test/v1/models", preset.ModelsUrl!.AbsoluteUri);
        Assert.Equal("literal-value", preset.Headers!.Single().Value);
    }

    [Fact]
    public void Parse_refuses_a_header_with_a_control_character_value()
    {
        string json = JsonWithProviders(
            """{ "id": "sample", "name": "Sample", "adapter": "openai-compatible", "baseUrl": "https://sample.example.test/v1", "headers": { "X-Refused": "line\nbreak" }, "keyVariable": "SAMPLE_API_KEY" }"""
        );

        InvalidDataException exception = AssertParseFails(json);

        Assert.Contains("X-Refused", exception.Message, StringComparison.Ordinal);
    }

    private static InvalidDataException AssertParseFails(string json) =>
        Assert.Throws<InvalidDataException>(() => ProviderPresets.Parse(json, Source));

    private static string JsonWithProviders(params string[] providers) =>
        $$"""
            {
              "schemaVersion": 2,
              "providers": [
                {{string.Join(",\n            ", providers)}}
              ]
            }
            """;
}
