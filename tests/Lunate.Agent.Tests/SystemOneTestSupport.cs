using System.Net;
using System.Text;
using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Agent.Tests;

internal static class SystemOneDecisionTestData
{
    public static DecisionRequest CreateRequest(JsonElement? providedState = null)
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        return new DecisionRequest(
            "typesafe/jev-1.14",
            providedState ?? state.RootElement.Clone(),
            [new DecisionQuestion("safe", "Is the call safe?", DecisionQuestionKind.Noul, ["yes", "no"])]
        );
    }
}

internal sealed class SystemOneRecordingHandler(
    string responseBody,
    HttpStatusCode statusCode = HttpStatusCode.OK
) : HttpMessageHandler
{
    public string? RequestBody { get; private set; }
    public Uri? RequestUri { get; private set; }
    public int RequestCount { get; private set; }
    public Dictionary<string, string[]>? RequestHeaders { get; private set; }
    public string[]? ContentEncoding { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        RequestUri = request.RequestUri;
        RequestHeaders = request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase
        );
        ContentEncoding = request.Content!.Headers.ContentEncoding.ToArray();
        RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed class SystemOneTransientHandler : HttpMessageHandler
{
    private const string Success =
        """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}""";

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        HttpResponseMessage response = RequestCount switch
        {
            1 => CreateTransient(HttpStatusCode.TooManyRequests),
            2 => CreateTransient((HttpStatusCode)529),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Success, Encoding.UTF8, "application/json"),
            },
        };
        return Task.FromResult(response);
    }

    private static HttpResponseMessage CreateTransient(HttpStatusCode statusCode)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.Zero
        );
        return response;
    }
}

internal sealed class SystemOneRetryAfterHandler(TimeSpan retryAfter) : HttpMessageHandler
{
    private const string Success =
        """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}""";

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        if (RequestCount > 1)
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Success, Encoding.UTF8, "application/json"),
                }
            );
        }

        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            retryAfter
        );
        return Task.FromResult(response);
    }
}

internal sealed class SystemOneRateLimitHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.Zero
        );
        return Task.FromResult(response);
    }
}
