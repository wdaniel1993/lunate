using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneDecisionClientTests
{
    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Request_uses_array_score_criteria_and_record_choice_criteria()
    {
        using JsonDocument state = JsonDocument.Parse("""{"path":"src"}""");
        DecisionRequest request = new(
            "typesafe/jev-1.14-20261014",
            state.RootElement.Clone(),
            [
                new(
                    "team",
                    "Choose the team.",
                    DecisionQuestionKind.Choice,
                    ["payments", "frontend"],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["payments"] = "Owns billing.",
                        ["frontend"] = "Owns the web interface.",
                    }
                ),
                new(
                    "urgency",
                    "Score urgency.",
                    DecisionQuestionKind.Score,
                    ["low", "high"],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["low"] = "Can wait.",
                        ["high"] = "Blocks revenue.",
                    }
                ),
            ]
        );
        using RecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14-20261014","answers":{"team":{"type":"choice","choice":"payments","probabilities":{"payments":0.77,"frontend":0.23},"confidence":0.54},"urgency":{"type":"score","score":1.77,"legend":{"0":"Can wait.","1":"Blocks revenue."},"probabilities":{"0":0.23,"1":0.77},"confidence":0.54}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOneProviderOptions(new Uri("https://provider.test/api/v1/systemone"))
        );

        DecisionResult result = await client.GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.Body!);
        JsonElement questions = sent.RootElement.GetProperty("questions");
        Assert.Equal("typesafe/jev-1.14-20261014", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal("src", sent.RootElement.GetProperty("state").GetProperty("path").GetString());
        Assert.Equal("choice", questions.GetProperty("team").GetProperty("type").GetString());
        Assert.Equal(
            "Owns billing.",
            questions.GetProperty("team").GetProperty("criteria").GetProperty("payments").GetString()
        );
        Assert.Equal("score", questions.GetProperty("urgency").GetProperty("type").GetString());
        Assert.Equal(
            JsonValueKind.Array,
            questions.GetProperty("urgency").GetProperty("criteria").ValueKind
        );
        Assert.Equal("payments", result.Results["team"].Answer);
        Assert.Equal("high", result.Results["urgency"].Answer);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
