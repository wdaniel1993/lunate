using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class DiscoverCommandTests
{
    private const string UrlTarget = "http://localhost:9999/v1";

    [Fact]
    public async Task Url_target_requests_models_and_returns_a_draft_without_writing_anything()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(
                HttpStatusCode.OK,
                """{ "data": [ { "id": "alpha" }, { "id": "beta" } ], "extra": 1 }"""
            )
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal($"{UrlTarget}/models", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.False(handler.Requests[0].Headers.Contains("Authorization"));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Root));

        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        Assert.Equal(2, draft.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement models = draft.RootElement.GetProperty("models");
        Assert.Equal(["alpha", "beta"], Ids(models));
        Assert.Equal(UrlTarget, models[0].GetProperty("endpoint").GetString());
        Assert.False(models[0].TryGetProperty("provider", out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Url_target_sends_no_credential_even_when_a_key_is_configured(
        bool fromEnvironment
    )
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "alpha" } ] }""")
        );
        bool storeRead = false;

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            namedKeySource: _ =>
            {
                storeRead = true;
                return "stored-key";
            },
            environment: name => fromEnvironment && name == "OPENAI_API_KEY" ? "env-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        AssertNoCredential(handler.Requests[0]);
        Assert.False(storeRead);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "OPENAI_API_KEY", "openai-key", "Authorization")]
    [InlineData("https://api.anthropic.com/v1", "ANTHROPIC_API_KEY", "anthropic-key", "x-api-key")]
    public async Task A_direct_preset_url_uses_that_presets_key_and_header(
        string url,
        string variable,
        string key,
        string header
    )
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "alpha" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            url,
            Catalog(temp),
            handler,
            environment: name => name == variable ? key : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal($"{url}/models", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(
            header == "Authorization" ? $"Bearer {key}" : key,
            handler.Requests[0].Headers.GetValues(header).Single()
        );
        Assert.False(
            handler
                .Requests[0]
                .Headers.Contains(header == "Authorization" ? "x-api-key" : "Authorization")
        );
    }

    [Fact]
    public async Task Name_target_uses_the_catalog_endpoint_and_provider()
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            """
            { "id": "local", "provider": "openai", "endpoint": "http://localhost:4321/v1", "contextWindow": 8192, "supportsTools": true }
            """
        );
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "tiny" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "local",
            ModelCatalog.Load(userFile),
            handler,
            environment: name => name == "OPENAI_API_KEY" ? "env-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "http://localhost:4321/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        AssertNoCredential(handler.Requests[0]);
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        JsonElement model = draft.RootElement.GetProperty("models")[0];
        Assert.Equal("openai", model.GetProperty("provider").GetString());
        Assert.Equal("http://localhost:4321/v1", model.GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task A_declared_gateway_endpoint_gets_no_credential()
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            """
            { "id": "gateway", "provider": "openai", "endpoint": "http://gateway.test/v1", "contextWindow": 8192, "supportsTools": true }
            """
        );
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "small" } ] }""")
        );
        bool storeRead = false;

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "gateway",
            ModelCatalog.Load(userFile),
            handler,
            namedKeySource: _ =>
            {
                storeRead = true;
                return "stored-openai-key";
            },
            environment: name => name == "OPENAI_API_KEY" ? "env-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("http://gateway.test/v1/models", handler.Requests[0].RequestUri!.AbsoluteUri);
        AssertNoCredential(handler.Requests[0]);
        Assert.False(storeRead);
    }

    [Fact]
    public async Task An_unknown_provider_endpoint_gets_no_credential()
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            """
            { "id": "custom", "provider": "custom", "endpoint": "http://localhost:5000/v1", "contextWindow": 8192, "supportsTools": true }
            """
        );
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "small" } ] }""")
        );
        bool storeRead = false;

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "custom",
            ModelCatalog.Load(userFile),
            handler,
            namedKeySource: _ =>
            {
                storeRead = true;
                return "stored-openai-key";
            },
            environment: name => name == "OPENAI_API_KEY" ? "env-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        AssertNoCredential(handler.Requests[0]);
        Assert.False(storeRead);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://api.openai.com/v1/proxy")]
    public async Task A_declared_preset_endpoint_uses_the_preset_key(string endpoint)
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            $$"""
            { "id": "own", "provider": "openai", "endpoint": "{{endpoint}}", "contextWindow": 8192, "supportsTools": true }
            """
        );
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "small" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "own",
            ModelCatalog.Load(userFile),
            handler,
            environment: name => name == "OPENAI_API_KEY" ? "sk-own" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal($"{endpoint}/models", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(
            "Bearer sk-own",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
        );
    }

    [Fact]
    public async Task Catalog_name_with_a_default_endpoint_uses_the_provider_default()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "gpt-4o-mini" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "gpt-4o-mini",
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "https://api.openai.com/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        JsonElement model = draft.RootElement.GetProperty("models")[0];
        Assert.Equal("openai", model.GetProperty("provider").GetString());
        Assert.False(model.TryGetProperty("endpoint", out _));
    }

    [Fact]
    public async Task Preset_target_uses_its_base_url_and_key_and_drafts_qualified_ids()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(
                HttpStatusCode.OK,
                """{ "data": [ { "id": "llama-3.3-70b" }, { "id": "groq/compound" } ] }"""
            )
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "groq",
            Catalog(temp),
            handler,
            environment: name => name == "GROQ_API_KEY" ? "gsk-test" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "https://api.groq.com/openai/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        Assert.Equal(
            "Bearer gsk-test",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
        );

        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        Assert.Equal(2, draft.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement models = draft.RootElement.GetProperty("models");
        Assert.Equal(["groq/llama-3.3-70b", "groq/groq/compound"], Ids(models));
        Assert.Equal("groq", models[0].GetProperty("provider").GetString());
        Assert.Equal("llama-3.3-70b", models[0].GetProperty("wireId").GetString());
        Assert.False(models[0].TryGetProperty("endpoint", out _));
        Assert.False(models[0].TryGetProperty("auth", out _));
    }

    [Fact]
    public async Task Preset_target_falls_back_to_the_stored_provider_key()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "llama-3.3-70b" } ] }""")
        );
        string? requested = null;

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "groq",
            Catalog(temp),
            handler,
            namedKeySource: name =>
            {
                requested = name;
                return "stored-groq-key";
            },
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("groq", requested);
        Assert.Equal(
            "Bearer stored-groq-key",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
        );
    }

    [Fact]
    public async Task Preset_target_accepts_the_google_key_alias()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "gemini-3-pro" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "google",
            Catalog(temp),
            handler,
            environment: name => name == "GEMINI_API_KEY" ? "gemini-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/openai/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        Assert.Equal(
            "Bearer gemini-key",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
        );
    }

    [Fact]
    public async Task Native_preset_target_keeps_bare_ids()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "gpt-4o" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "openai",
            Catalog(temp),
            handler,
            environment: name => name == "OPENAI_API_KEY" ? "sk-test" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "https://api.openai.com/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        JsonElement model = draft.RootElement.GetProperty("models")[0];
        Assert.Equal("gpt-4o", model.GetProperty("id").GetString());
        Assert.False(model.TryGetProperty("wireId", out _));
    }

    [Fact]
    public async Task A_preset_catalog_model_uses_the_preset_and_drafts_qualified_ids()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "allam-2-7b" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "groq/allam-2-7b",
            Catalog(temp),
            handler,
            environment: name => name == "GROQ_API_KEY" ? "gsk-test" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "https://api.groq.com/openai/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        JsonElement model = draft.RootElement.GetProperty("models")[0];
        Assert.Equal("groq/allam-2-7b", model.GetProperty("id").GetString());
        Assert.Equal("groq", model.GetProperty("provider").GetString());
        Assert.Equal("allam-2-7b", model.GetProperty("wireId").GetString());
    }

    [Fact]
    public async Task Auth_reference_sends_the_named_key()
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            """
            { "id": "work", "provider": "openai", "endpoint": "https://example.test/v1", "contextWindow": 1, "supportsTools": true, "auth": "work" }
            """
        );
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "w" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "work",
            ModelCatalog.Load(userFile),
            handler,
            namedKeySource: name => name == "work" ? "work-key" : null,
            environment: name => name == "OPENAI_API_KEY" ? "preset-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "Bearer work-key",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
        );
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        Assert.Equal(
            "work",
            draft.RootElement.GetProperty("models")[0].GetProperty("auth").GetString()
        );
    }

    [Fact]
    public async Task A_missing_auth_reference_names_the_key()
    {
        using var temp = new TempDirectory();
        string userFile = WriteCatalog(
            temp,
            """
            { "id": "work", "provider": "openai", "endpoint": "https://example.test/v1", "contextWindow": 1, "supportsTools": true, "auth": "missing" }
            """
        );
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "data": [] }"""));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "work",
            ModelCatalog.Load(userFile),
            handler,
            namedKeySource: _ => null,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains("missing", result.Error, StringComparison.Ordinal);
        Assert.Contains("auth.json", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_data_is_a_clear_error()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "models": [] }"""));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains(UrlTarget, result.Error, StringComparison.Ordinal);
        Assert.Contains("data", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_success_status_names_the_target_and_status()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.ServiceUnavailable, """{ "error": "down" }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains(UrlTarget, result.Error, StringComparison.Ordinal);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    public async Task A_rejection_without_a_credential_explains_the_opt_in(
        HttpStatusCode status,
        string code
    )
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ => Json(status, """{ "error": "no key" }"""));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: name => name == "OPENAI_API_KEY" ? "sk-env-secret" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains($"{UrlTarget}/models", result.Error, StringComparison.Ordinal);
        Assert.Contains(code, result.Error, StringComparison.Ordinal);
        Assert.Contains("No key was sent", result.Error, StringComparison.Ordinal);
        Assert.Contains("not the provider's own", result.Error, StringComparison.Ordinal);
        Assert.Contains("'auth' reference", result.Error, StringComparison.Ordinal);
        Assert.Equal(2, result.Error!.Split("No key was sent").Length);
        Assert.DoesNotContain("sk-env-secret", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_json_is_a_clear_error()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "not json at all"));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains(UrlTarget, result.Error, StringComparison.Ordinal);
        Assert.Contains("JSON", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ambiguous_bare_model_names_its_candidates_and_makes_no_request()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "data": [] }"""));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "deepseek-v4-flash",
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains("deepseek-v4-flash", result.Error, StringComparison.Ordinal);
        Assert.Contains("deepseek/deepseek-v4-flash", result.Error, StringComparison.Ordinal);
        Assert.Contains("opencode/deepseek-v4-flash", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_unknown_name_names_the_target_and_points_at_the_catalog()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "data": [] }"""));

        DiscoverResult result = await DiscoverCommand.RunAsync(
            "nope",
            Catalog(temp),
            handler,
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.False(result.Succeeded);
        Assert.Contains("nope", result.Error, StringComparison.Ordinal);
        Assert.Contains("models.json", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private static ModelCatalog Catalog(TempDirectory temp) =>
        ModelCatalog.Load(temp.File("models.json"));

    private static string WriteCatalog(TempDirectory temp, string model)
    {
        string path = temp.File("models.json");
        File.WriteAllText(path, $$"""{ "schemaVersion": 1, "models": [ {{model}} ] }""");
        return path;
    }

    private static string[] Ids(JsonElement models) =>
        [.. models.EnumerateArray().Select(model => model.GetProperty("id").GetString()!)];

    private static void AssertNoCredential(HttpRequestMessage request)
    {
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("x-api-key"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
