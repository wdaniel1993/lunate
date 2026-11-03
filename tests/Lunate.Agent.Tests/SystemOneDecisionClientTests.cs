using System.Net;
using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneDecisionClientTests
{
    [Fact]
    public async Task GetDecisionAsync_translates_a_noul_exchange()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );
        using JsonDocument state = JsonDocument.Parse("{\"tool\":\"read\"}");
        DecisionRequest request = new(
            "typesafe/jev-1.14",
            state.RootElement,
            [
                new DecisionQuestion(
                    "safe",
                    "Is the call safe?",
                    DecisionQuestionKind.Noul,
                    ["yes", "no"],
                    new Dictionary<string, string>
                    {
                        ["true"] = "The call is safe.",
                        ["false"] = "The call is unsafe.",
                    }
                ),
            ]
        );

        DecisionResult result = await client.GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("https://example.test/api/v1/systemone", handler.RequestUri!.ToString());
        Assert.Equal("typesafe/jev-1.14", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            state.RootElement.GetRawText(),
            sent.RootElement.GetProperty("state").GetRawText()
        );
        JsonElement question = sent.RootElement.GetProperty("questions").GetProperty("safe");
        Assert.Equal("noul", question.GetProperty("type").GetString());
        Assert.Equal("The call is safe.", question.GetProperty("criteria").GetProperty("true").GetString());
        Assert.Equal("yes", result.Results["safe"].Answer);
        Assert.Equal(0.91, result.Results["safe"].Probability);
        Assert.Equal(0.82, result.Results["safe"].Confidence, 10);
    }

    [Fact]
    public async Task GetDecisionAsync_maps_max_tokens_error_to_state_size_failure()
    {
        using SystemOneRecordingHandler handler = new(
            """{"error":{"code":"max_tokens_exceeded","message":"max_tokens_exceeded"}}""",
            HttpStatusCode.BadRequest
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );

        DecisionTransportException exception = await Assert.ThrowsAsync<DecisionTransportException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("state too large for this model", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_surfaces_the_field_from_a_422_response()
    {
        using SystemOneRecordingHandler handler = new(
            """{"error":{"message":"[{\"path\":[\"questions\",\"safe\",\"criteria\"],\"message\":\"Invalid input\"}]"}}""",
            HttpStatusCode.UnprocessableEntity
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );

        DecisionTransportException exception = await Assert.ThrowsAsync<DecisionTransportException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("questions.safe.criteria", exception.Field);
        Assert.Contains("Invalid input", exception.ProviderMessage, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_maps_413_to_named_body_limit_error()
    {
        using SystemOneRecordingHandler handler = new(
            """{"detail":{"message":"request body too large"}}""",
            HttpStatusCode.RequestEntityTooLarge
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );

        DecisionTransportException exception = await Assert.ThrowsAsync<DecisionTransportException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("32 MiB request limit", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDecisionAsync_applies_preset_image_headers_for_image_state()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(
                new Uri("https://example.test/api/v1/systemone"),
                new Dictionary<string, string>
                {
                    ["TypeSafe-Danger-Zone-Images"] = "true",
                }
            )
        );
        using JsonDocument state = JsonDocument.Parse(
            """{"parts":[{"type":"image","mimeType":"image/png","data":"AQ=="}]}"""
        );

        await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("true", handler.RequestHeaders!["TypeSafe-Danger-Zone-Images"].Single());
    }

    [Fact]
    public async Task GetDecisionAsync_omits_preset_image_headers_for_text_state()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(
                new Uri("https://example.test/api/v1/systemone"),
                new Dictionary<string, string>
                {
                    ["TypeSafe-Danger-Zone-Images"] = "true",
                }
            )
        );

        await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("TypeSafe-Danger-Zone-Images", handler.RequestHeaders!.Keys);
    }

}
