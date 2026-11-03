using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneDecisionClientTests
{
    [Fact]
    public async Task GetDecisionAsync_translates_a_noul_exchange()
    {
        using RecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new Uri("https://example.test/api/v1/systemone")
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

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
