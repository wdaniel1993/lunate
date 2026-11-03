using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai.SystemOne;
using Lunate.Contracts;

namespace Lunate.Coding.Tests;

public sealed class NativeDecisionCompatibilityTests
{
    [Fact]
    public async Task Chat_service_exposes_the_legacy_interface_as_a_decision_client_forwarder()
    {
        using ResponseHandler handler = new();
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient native = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );
        SystemOneChatFacade facade = new("model-x", native);
        NativeDecisionClientCompatibilityChatClient client = new(facade, "model-x");
        INativeDecisionClient legacy = Assert.IsAssignableFrom<INativeDecisionClient>(
            client.GetService(typeof(INativeDecisionClient))
        );
        using JsonDocument state = JsonDocument.Parse("{}");

        string response = await legacy.GetDecisionAsync(
            new Lunate.Contracts.DecisionRequest(
                state.RootElement.Clone(),
                [new Lunate.Contracts.DecisionQuestion("safe", "Is it safe?", DecisionQuestionKind.YesNo)]
            ),
            TestContext.Current.CancellationToken
        );

        using JsonDocument parsed = JsonDocument.Parse(response);
        Assert.Equal(
            "yes",
            parsed.RootElement.GetProperty("results").GetProperty("safe").GetProperty("answer").GetString()
        );
        Assert.Equal("model-x", handler.ModelId);
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        public string? ModelId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken)
            );
            ModelId = body.RootElement.GetProperty("model").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"model-x","answers":{"safe":{"type":"noul","noul":0.8}}}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
