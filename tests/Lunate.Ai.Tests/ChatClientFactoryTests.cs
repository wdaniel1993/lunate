using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Reflection;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Lunate.Ai.Tests;

[Collection(ChatClientFactoryEnvCollection.Name)]
public sealed class ChatClientFactoryTests
{
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private const string AnthropicApiKeyVariable = "ANTHROPIC_API_KEY";
    private const string OpenTelemetryOptInVariable = "LUNATE_OTEL";
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public async Task Create_with_telemetry_enabled_observes_telemetry_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: true);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(new[] { "telemetry", "logging", "recorder", "provider" }, sequence);
    }

    [Fact]
    public async Task Create_with_telemetry_disabled_observes_logging_recorder_provider_in_order()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        var factory = CreateFactory(sequence, enableOpenTelemetry: false);

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(new[] { "logging", "recorder", "provider" }, sequence);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task Create_enables_telemetry_when_LUNATE_OTEL_is_truthy(string optIn)
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, optIn),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_enables_telemetry_when_OTLP_endpoint_is_set()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, "http://localhost:4317")
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Contains("telemetry", sequence);
    }

    [Fact]
    public async Task Create_keeps_telemetry_disabled_without_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactoryWithoutExplicitTelemetry(sequence).Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("telemetry", sequence);
    }

    [Fact]
    public async Task Create_explicit_telemetry_disabled_overrides_environment_opt_in()
    {
        var sequence = new List<string>();
        using var listener = ListenForTelemetry(sequence);
        using var environment = new EnvironmentScope(
            (OpenTelemetryOptInVariable, "1"),
            (OtlpEndpointVariable, null)
        );

        IChatClient client = CreateFactory(sequence, enableOpenTelemetry: false)
            .Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("telemetry", sequence);
    }

    [Fact]
    public async Task Create_never_wraps_the_provider_in_function_invoking_chat_client()
    {
        var sequence = new List<string>();
        MarkerChatClient? recorder = null;
        var factory = new ChatClientFactory(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            enableOpenTelemetry: true,
            providerClientFactory: (_, _) => new ProviderStub(sequence),
            recorderDecorator: inner => recorder = new MarkerChatClient("recorder", sequence, inner)
        );

        IChatClient client = factory.Create(TestModel());
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.NotNull(recorder);
        IChatClient[] chain = [.. WalkChain(client)];
        Assert.DoesNotContain(chain, candidate => candidate is FunctionInvokingChatClient);
        int recorderIndex = Array.FindIndex(
            chain,
            candidate => ReferenceEquals(candidate, recorder)
        );
        Assert.True(recorderIndex > 0);
        Assert.IsType<StreamAccumulator>(chain[recorderIndex - 1]);
        Assert.IsType<ProviderStub>(chain[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_places_the_pipeline_layers_outermost_first(bool enableOpenTelemetry)
    {
        IChatClient client = CreateFactory([], enableOpenTelemetry).Create(TestModel());

        IChatClient[] chain = [.. WalkChain(client)];
        string[] expected = enableOpenTelemetry
            ?
            [
                "OpenTelemetryChatClient",
                "LoggingChatClient",
                "StreamAccumulator",
                "MarkerChatClient",
                "ProviderStub",
            ]
            : ["LoggingChatClient", "StreamAccumulator", "MarkerChatClient", "ProviderStub"];

        Assert.Equal(expected, chain.Select(candidate => candidate.GetType().Name));
    }

    [Fact]
    public void Constructor_throws_when_logger_factory_is_null()
    {
        Assert.Throws<ArgumentNullException>("loggerFactory", () => new ChatClientFactory(null!));
    }

    [Fact]
    public void Create_throws_when_model_is_null()
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        Assert.Throws<ArgumentNullException>("model", () => factory.Create(null!));
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("Gemini")]
    public void Create_throws_NotSupportedException_for_unknown_provider(string provider)
    {
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("gemini-2.5-pro", provider, null, 128_000, true);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            factory.Create(model)
        );

        Assert.Contains(provider, exception.Message, StringComparison.Ordinal);
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            Assert.Contains(preset.Id, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Create_assigns_the_model_endpoint_to_the_provider_client()
    {
        using var environment = new EnvironmentScope(
            (OpenAiApiKeyVariable, "test-key"),
            (OpenTelemetryOptInVariable, null),
            (OtlpEndpointVariable, null)
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("https://example.test/v1");
        var model = new ModelInfo("gpt-4o-mini", "openai", endpoint, 128_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Theory]
    [InlineData("openrouter", "openrouter/aion-labs/aion-2.0")]
    [InlineData("deepseek", "deepseek/deepseek-v4-flash")]
    [InlineData("moonshotai", "moonshotai/kimi-k2.6")]
    [InlineData("zai", "zai/glm-4.5")]
    [InlineData("opencode", "opencode/beam-free")]
    [InlineData("opencode-go", "opencode-go/beam-free")]
    [InlineData("groq", "groq/allam-2-7b")]
    [InlineData("togetherai", "togetherai/LiquidAI/LFM2-24B-A2B")]
    [InlineData("xai", "xai/grok-4.20-0309-non-reasoning")]
    [InlineData("google", "google/deep-research-max-preview-04-2026")]
    public void Create_routes_each_compatible_preset_to_its_base_url_and_wire_id(
        string presetId,
        string modelId
    )
    {
        ProviderPreset preset = ProviderPresets.Find(presetId)!;
        using var environment = new EnvironmentScope((preset.KeyVariable, "preset-test-key"));
        ModelInfo model = LoadCatalog().Find(modelId)!;
        Assert.NotNull(model.WireId);

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(preset.BaseUrl, metadata.ProviderUri);
        Assert.Equal(model.WireId, metadata.DefaultModelId);
    }

    [Fact]
    public void Create_sends_a_qualified_preset_id_without_its_provider_prefix()
    {
        using var environment = new EnvironmentScope(
            (ProviderPresets.Find("groq")!.KeyVariable, "test-key")
        );
        var model = new ModelInfo("groq/llama-custom", "groq", null, 128_000, true);

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("llama-custom", metadata.DefaultModelId);
    }

    [Fact]
    public void Create_keeps_the_openai_preset_on_the_sdk_default_endpoint_and_bare_id()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "test-key"));
        ModelInfo model = LoadCatalog().Find("gpt-4o-mini")!;

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("openai", metadata.ProviderName);
        Assert.Equal(model.Id, metadata.DefaultModelId);
        Assert.Null(model.WireId);
    }

    [Fact]
    public void Create_resolves_a_preset_for_a_catalog_entry_whose_provider_differs_in_case()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("models.json"),
            """
            {
              "schemaVersion": 1,
              "models": [
                { "id": "gpt-4o-mini", "provider": "OpenAI", "contextWindow": 128000, "supportsTools": true }
              ]
            }
            """
        );
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "test-key"));
        ModelInfo model = ModelCatalog.Load(temp.File("models.json")).Find("gpt-4o-mini")!;
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("gpt-4o-mini", metadata.DefaultModelId);
    }

    [Fact]
    public void Create_prefers_the_model_endpoint_over_the_preset_base_url()
    {
        using var environment = new EnvironmentScope(
            (ProviderPresets.Find("groq")!.KeyVariable, null)
        );
        var endpoint = new Uri("http://localhost:4321/v1");
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")! with { Endpoint = endpoint };

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_throws_InvalidOperationException_when_openai_api_key_is_not_set()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));

        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(model)
        );

        Assert.Contains(OpenAiApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_with_a_custom_endpoint_and_no_openai_api_key_uses_a_placeholder_credential()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("http://localhost:11434/v1");
        var model = new ModelInfo("local-llama", "openai", endpoint, 8192, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_openai_client_options_disable_transport_retries()
    {
        OpenAIClientOptions options = ChatClientFactory.CreateOpenAiClientOptions(TestModel());

        ClientRetryPolicy policy = Assert.IsType<ClientRetryPolicy>(options.RetryPolicy);
        FieldInfo? maxRetries = typeof(ClientRetryPolicy).GetField(
            "_maxRetries",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(maxRetries);
        Assert.Equal(0, maxRetries.GetValue(policy));
    }

    [Fact]
    public void Create_with_anthropic_provider_builds_the_client_without_network()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", null, 1_000_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal("anthropic", metadata.ProviderName);
        Assert.Equal(model.Id, metadata.DefaultModelId);
    }

    [Fact]
    public void Create_assigns_the_model_endpoint_to_the_anthropic_base_url()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var endpoint = new Uri("https://anthropic.example.test");
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", endpoint, 1_000_000, true);

        IChatClient client = factory.Create(model);
        var metadata = (ChatClientMetadata?)client.GetService(typeof(ChatClientMetadata));

        Assert.NotNull(metadata);
        Assert.Equal(endpoint, metadata.ProviderUri);
    }

    [Fact]
    public void Create_anthropic_client_disables_transport_retries()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            new Uri("https://anthropic.example.test"),
            1_000_000,
            true
        );

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(model);

        Assert.Equal(0, client.MaxRetries);
        Assert.Equal(ChatClientFactory.PlaceholderCredential, client.ApiKey);
        Assert.Equal(model.Endpoint!.AbsoluteUri, client.BaseUrl);
    }

    [Fact]
    public void Create_anthropic_client_with_default_endpoint_uses_the_environment_key()
    {
        using var environment = new EnvironmentScope(
            (AnthropicApiKeyVariable, "anthropic-test-key")
        );
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", null, 1_000_000, true);

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(model);

        Assert.Equal("anthropic-test-key", client.ApiKey);
    }

    [Fact]
    public void Create_anthropic_client_with_a_custom_endpoint_and_no_key_uses_the_placeholder()
    {
        using var environment = new EnvironmentScope((AnthropicApiKeyVariable, null));
        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            new Uri("https://anthropic.example.test"),
            1_000_000,
            true
        );

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(model);

        Assert.Equal(ChatClientFactory.PlaceholderCredential, client.ApiKey);
    }

    [Fact]
    public void ResolveApiKey_uses_the_placeholder_for_custom_endpoints_even_with_a_key_set()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "test-key"));
        var model = new ModelInfo(
            "gpt-4o-mini",
            "openai",
            new Uri("https://example.test/v1"),
            128_000,
            true
        );

        string credential = ChatClientFactory.ResolveApiKey(model, ProviderPresets.Find("openai")!);

        Assert.Equal(ChatClientFactory.PlaceholderCredential, credential);
    }

    [Fact]
    public void ResolveApiKey_uses_the_environment_key_for_default_endpoints()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "test-key"));
        var model = new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true);

        string credential = ChatClientFactory.ResolveApiKey(model, ProviderPresets.Find("openai")!);

        Assert.Equal("test-key", credential);
    }

    [Fact]
    public void ResolveApiKey_with_an_auth_reference_resolves_the_named_key()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "env-key"));
        var model = new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true, AuthRef: "work");

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            ProviderPresets.Find("openai")!,
            name => name == "work" ? "work-key" : null
        );

        Assert.Equal("work-key", credential);
    }

    [Fact]
    public void ResolveApiKey_with_an_auth_reference_uses_the_named_key_for_a_custom_endpoint()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, "env-key"));
        var model = new ModelInfo(
            "local-llama",
            "openai",
            new Uri("http://localhost:11434/v1"),
            8192,
            true,
            AuthRef: "work"
        );

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            ProviderPresets.Find("openai")!,
            name => name == "work" ? "work-key" : null
        );

        Assert.Equal("work-key", credential);
    }

    [Fact]
    public void ResolveApiKey_with_an_unresolvable_auth_reference_names_the_key_and_auth_json()
    {
        var model = new ModelInfo(
            "local-llama",
            "openai",
            new Uri("http://localhost:11434/v1"),
            8192,
            true,
            AuthRef: "missing"
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ChatClientFactory.ResolveApiKey(model, ProviderPresets.Find("openai")!, _ => null)
        );

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveApiKey_without_a_reference_falls_back_to_the_stored_provider_key()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));
        var model = new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true);

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            ProviderPresets.Find("openai")!,
            name => name == "openai" ? "stored-key" : null
        );

        Assert.Equal("stored-key", credential);
    }

    [Fact]
    public void ResolveApiKey_without_any_key_names_the_variable_and_auth_json()
    {
        using var environment = new EnvironmentScope((OpenAiApiKeyVariable, null));
        var model = new ModelInfo("gpt-4o-mini", "openai", null, 128_000, true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ChatClientFactory.ResolveApiKey(model, ProviderPresets.Find("openai")!, _ => null)
        );

        Assert.Contains(OpenAiApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveApiKey_prefers_the_preset_environment_key_over_the_store()
    {
        ProviderPreset groq = ProviderPresets.Find("groq")!;
        using var environment = new EnvironmentScope((groq.KeyVariable, "env-key"));
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")!;
        bool storeRead = false;

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            groq,
            _ =>
            {
                storeRead = true;
                return "stored-key";
            }
        );

        Assert.Equal("env-key", credential);
        Assert.False(storeRead);
    }

    [Fact]
    public void ResolveApiKey_without_a_preset_key_uses_the_store_under_the_preset_id()
    {
        ProviderPreset groq = ProviderPresets.Find("groq")!;
        using var environment = new EnvironmentScope((groq.KeyVariable, null));
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")!;
        string? requested = null;

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            groq,
            name =>
            {
                requested = name;
                return "stored-key";
            }
        );

        Assert.Equal("stored-key", credential);
        Assert.Equal("groq", requested);
    }

    [Fact]
    public void ResolveApiKey_with_an_auth_reference_wins_over_the_preset_key()
    {
        ProviderPreset groq = ProviderPresets.Find("groq")!;
        using var environment = new EnvironmentScope((groq.KeyVariable, "env-key"));
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")! with { AuthRef = "work" };

        string credential = ChatClientFactory.ResolveApiKey(
            model,
            groq,
            name => name == "work" ? "work-key" : null
        );

        Assert.Equal("work-key", credential);
    }

    [Fact]
    public void ResolveApiKey_uses_the_placeholder_for_a_preset_endpoint_even_with_its_key_set()
    {
        ProviderPreset groq = ProviderPresets.Find("groq")!;
        using var environment = new EnvironmentScope((groq.KeyVariable, "env-key"));
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")! with
        {
            Endpoint = new Uri("http://localhost:4321/v1"),
        };

        string credential = ChatClientFactory.ResolveApiKey(model, groq, _ => "stored-key");

        Assert.Equal(ChatClientFactory.PlaceholderCredential, credential);
    }

    [Fact]
    public void ResolveApiKey_accepts_the_google_alias_when_its_variable_is_unset()
    {
        ProviderPreset google = ProviderPresets.Find("google")!;
        using var environment = new EnvironmentScope(
            (google.KeyVariable, null),
            (google.KeyVariableAlias!, "gemini-key")
        );
        ModelInfo model = LoadCatalog().Find("google/deep-research-max-preview-04-2026")!;

        string credential = ChatClientFactory.ResolveApiKey(model, google, _ => "stored-key");

        Assert.Equal("gemini-key", credential);
    }

    [Fact]
    public void ResolveApiKey_prefers_the_google_variable_over_its_alias()
    {
        ProviderPreset google = ProviderPresets.Find("google")!;
        using var environment = new EnvironmentScope(
            (google.KeyVariable, "google-key"),
            (google.KeyVariableAlias!, "gemini-key")
        );
        ModelInfo model = LoadCatalog().Find("google/deep-research-max-preview-04-2026")!;

        string credential = ChatClientFactory.ResolveApiKey(model, google, _ => "stored-key");

        Assert.Equal("google-key", credential);
    }

    [Fact]
    public void ResolveApiKey_without_a_preset_key_names_its_variable_and_store_name()
    {
        ProviderPreset groq = ProviderPresets.Find("groq")!;
        using var environment = new EnvironmentScope((groq.KeyVariable, null));
        ModelInfo model = LoadCatalog().Find("groq/allam-2-7b")!;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ChatClientFactory.ResolveApiKey(model, groq, _ => null)
        );

        Assert.Contains("GROQ_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'groq'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveApiKey_names_both_google_variables_when_neither_is_set()
    {
        ProviderPreset google = ProviderPresets.Find("google")!;
        using var environment = new EnvironmentScope(
            (google.KeyVariable, null),
            (google.KeyVariableAlias!, null)
        );
        ModelInfo model = LoadCatalog().Find("google/deep-research-max-preview-04-2026")!;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ChatClientFactory.ResolveApiKey(model, google, _ => null)
        );

        Assert.Contains("GOOGLE_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GEMINI_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_anthropic_client_with_an_auth_reference_uses_the_named_key()
    {
        using var environment = new EnvironmentScope((AnthropicApiKeyVariable, null));
        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            new Uri("https://anthropic.example.test"),
            1_000_000,
            true,
            AuthRef: "work"
        );

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(
            model,
            name => name == "work" ? "work-key" : null
        );

        Assert.Equal("work-key", client.ApiKey);
    }

    [Fact]
    public void Create_anthropic_client_without_a_reference_still_uses_the_placeholder_for_custom_endpoints()
    {
        using var environment = new EnvironmentScope((AnthropicApiKeyVariable, "env-key"));
        var model = new ModelInfo(
            "claude-sonnet-5-5",
            "anthropic",
            new Uri("https://anthropic.example.test"),
            1_000_000,
            true
        );

        AnthropicClient client = ChatClientFactory.CreateAnthropicClient(
            model,
            name => name == "anthropic" ? "stored-key" : null
        );

        Assert.Equal(ChatClientFactory.PlaceholderCredential, client.ApiKey);
    }

    [Fact]
    public void Create_throws_InvalidOperationException_when_anthropic_api_key_is_not_set()
    {
        using var environment = new EnvironmentScope((AnthropicApiKeyVariable, null));
        var factory = new ChatClientFactory(new MarkingLoggerFactory(static () => { }));
        var model = new ModelInfo("claude-sonnet-5-5", "anthropic", null, 1_000_000, true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(model)
        );

        Assert.Contains(AnthropicApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("auth.json", exception.Message, StringComparison.Ordinal);
    }

    private static ChatClientFactory CreateFactory(
        List<string> sequence,
        bool enableOpenTelemetry
    ) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            enableOpenTelemetry,
            (_, _) => new ProviderStub(sequence),
            inner => new MarkerChatClient("recorder", sequence, inner)
        );

    private static ChatClientFactory CreateFactoryWithoutExplicitTelemetry(List<string> sequence) =>
        new(
            new MarkingLoggerFactory(() => sequence.Add("logging")),
            providerClientFactory: (_, _) => new ProviderStub(sequence),
            recorderDecorator: inner => new MarkerChatClient("recorder", sequence, inner)
        );

    private static ModelInfo TestModel() => new("gpt-4o-mini", "openai", null, 128_000, true);

    private static ModelCatalog LoadCatalog() =>
        ModelCatalog.Load(Path.Combine(Path.GetTempPath(), "lunate-ai-tests-missing-catalog.json"));

    private static IEnumerable<IChatClient> WalkChain(IChatClient client)
    {
        IChatClient? current = client;
        while (current is not null)
        {
            yield return current;
            current = InnerClientOf(current);
        }
    }

    private static IChatClient? InnerClientOf(IChatClient client)
    {
        for (Type? type = client.GetType(); type is not null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                "InnerClient",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (property is not null)
            {
                return (IChatClient?)property.GetValue(client);
            }
        }

        return null;
    }

    private static ActivityListener ListenForTelemetry(List<string> sequence)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = _ => sequence.Add("telemetry"),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class MarkerChatClient(string marker, List<string> sequence, IChatClient inner)
        : DelegatingChatClient(inner)
    {
        public IChatClient Inner => InnerClient;

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            sequence.Add(marker);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }

    private sealed class ProviderStub(List<string> sequence) : IChatClient
    {
        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            sequence.Add("provider");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
