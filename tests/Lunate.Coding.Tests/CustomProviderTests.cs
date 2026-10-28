using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class CustomProviderTests
{
    [Theory]
    [InlineData("https://example.com/v1", "", "model id")]
    [InlineData("http://example.com/v1", "m", "http")]
    [InlineData("ftp://example.com/v1", "m", "ftp")]
    [InlineData("not-a-url", "m", "absolute")]
    [InlineData("", "m", "base URL")]
    public void Invalid_endpoint_input_is_refused_with_a_reason(
        string baseUrl,
        string model,
        string expected
    )
    {
        bool created = CustomProvider.TryCreate(
            "work",
            baseUrl,
            model,
            null,
            out CustomProvider? provider,
            out string? error
        );

        Assert.False(created);
        Assert.Null(provider);
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "provider id")]
    [InlineData("   ", "provider id")]
    [InlineData("bad id", "provider id")]
    [InlineData("bad/id", "provider id")]
    [InlineData("openai", "built-in")]
    public void Invalid_provider_id_is_refused_with_a_reason(string id, string expected)
    {
        bool created = CustomProvider.TryCreate(
            id,
            "https://example.com/v1",
            "m",
            null,
            out CustomProvider? provider,
            out string? error
        );

        Assert.False(created);
        Assert.Null(provider);
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        if (id.Trim().Length > 0)
        {
            Assert.DoesNotContain(id.Trim(), error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("https://example.com/v1")]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://localhost:8080/v1")]
    [InlineData("http://[::1]:8080/v1")]
    public void An_https_or_loopback_http_url_is_accepted(string baseUrl)
    {
        bool created = CustomProvider.TryCreate(
            "work",
            baseUrl,
            "m",
            null,
            out CustomProvider? provider,
            out string? error
        );

        Assert.True(created);
        Assert.Null(error);
        Assert.NotNull(provider);
        Assert.Equal("work", provider.Id);
        Assert.Equal(new Uri(baseUrl), provider.BaseUrl);
        Assert.Equal("m", provider.Model);
        Assert.Equal("work/m", provider.CatalogName);
    }

    [Fact]
    public void Inputs_are_trimmed_and_the_catalog_name_defaults_to_id_slash_model()
    {
        bool created = CustomProvider.TryCreate(
            " work ",
            " https://example.com/v1 ",
            " m ",
            "  ",
            out CustomProvider? provider,
            out _
        );

        Assert.True(created);
        Assert.NotNull(provider);
        Assert.Equal("work", provider.Id);
        Assert.Equal("m", provider.Model);
        Assert.Equal("work/m", provider.CatalogName);
    }

    [Fact]
    public void An_explicit_catalog_name_is_kept()
    {
        bool created = CustomProvider.TryCreate(
            "work",
            "https://example.com/v1",
            "m",
            " work/m-v2 ",
            out CustomProvider? provider,
            out _
        );

        Assert.True(created);
        Assert.Equal("work/m-v2", provider!.CatalogName);
    }

    [Fact]
    public void ToModelInfo_maps_the_provider_onto_the_native_openai_adapter()
    {
        var provider = new CustomProvider(
            "work",
            new Uri("https://example.com/v1"),
            "qwen2.5-coder",
            "work/qwen2.5-coder"
        );

        ModelInfo model = provider.ToModelInfo();

        Assert.Equal("work/qwen2.5-coder", model.Id);
        Assert.Equal("openai", model.Provider);
        Assert.Equal(new Uri("https://example.com/v1"), model.Endpoint);
        Assert.Equal(0, model.ContextWindow);
        Assert.True(model.SupportsTools);
        Assert.Equal("work", model.AuthRef);
        Assert.Equal("qwen2.5-coder", model.WireId);
    }
}
