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
        Assert.Equal(1, draft.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement models = draft.RootElement.GetProperty("models");
        Assert.Equal(["alpha", "beta"], Ids(models));
        Assert.Equal(UrlTarget, models[0].GetProperty("endpoint").GetString());
        Assert.False(models[0].TryGetProperty("provider", out _));
    }

    [Fact]
    public async Task Url_target_uses_the_environment_key_when_set()
    {
        using var temp = new TempDirectory();
        using var handler = new FakeHandler(_ =>
            Json(HttpStatusCode.OK, """{ "data": [ { "id": "alpha" } ] }""")
        );

        DiscoverResult result = await DiscoverCommand.RunAsync(
            UrlTarget,
            Catalog(temp),
            handler,
            environment: name => name == "OPENAI_API_KEY" ? "env-key" : null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "Bearer env-key",
            handler.Requests[0].Headers.GetValues("Authorization").Single()
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
            environment: _ => null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            "http://localhost:4321/v1/models",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        using JsonDocument draft = JsonDocument.Parse(result.Draft);
        JsonElement model = draft.RootElement.GetProperty("models")[0];
        Assert.Equal("openai", model.GetProperty("provider").GetString());
        Assert.Equal("http://localhost:4321/v1", model.GetProperty("endpoint").GetString());
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
            environment: _ => null,
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
