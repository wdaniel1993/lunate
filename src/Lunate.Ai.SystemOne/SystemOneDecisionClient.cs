using System.Net.Http.Headers;
using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Ai.SystemOne;

internal sealed class SystemOneDecisionClient(HttpClient httpClient, SystemOnePreset preset)
    : IDecisionClient
{
    public async Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        DecisionRequestValidator.Validate(request);

        string modelId = options?.ModelId ?? request.ModelId;
        byte[] body = SystemOneRequestMapper.Serialize(request, modelId);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpRequestMessage message = CreateMessage(body, request);
            using HttpResponseMessage response = await httpClient
                .SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                SystemOneResponse mapped = SystemOneResponseMapper.Map(responseBody, request);
                DecisionResult parsed = DecisionResponseParser.Parse(
                    mapped.ResultsJson,
                    request,
                    modelId,
                    "native"
                );
                return parsed with { Usage = mapped.Usage };
            }

            if (!IsRetryable(response.StatusCode) || attempt == 2)
            {
                throw SystemOneErrorMapper.Create(response.StatusCode, responseBody);
            }
            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException("System One request exhausted its retry attempts.");
    }

    private HttpRequestMessage CreateMessage(byte[] body, DecisionRequest request)
    {
        bool isCompressed = body.Length > 64 * 1024;
        if (isCompressed)
        {
            body = SystemOneRequestContent.Gzip(body);
        }
        using HttpRequestMessage message = new(HttpMethod.Post, preset.Endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (isCompressed)
        {
            message.Content.Headers.ContentEncoding.Add("gzip");
        }
        if (SystemOneRequestMapper.ContainsImagePart(request.State))
        {
            if (preset.ImageHeaders is { } imageHeaders)
            {
                foreach ((string name, string value) in imageHeaders)
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }
        return message;
    }

    public object? GetService(Type serviceType) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    private static bool IsRetryable(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests || (int)statusCode == 529;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        double exponentialMilliseconds = 250 * Math.Pow(2, attempt);
        TimeSpan delay = TimeSpan.FromMilliseconds(
            exponentialMilliseconds + Random.Shared.NextDouble() * exponentialMilliseconds
        );
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - DateTimeOffset.UtcNow;
        }
        return retryAfter > delay ? retryAfter.Value : delay;
    }
}
