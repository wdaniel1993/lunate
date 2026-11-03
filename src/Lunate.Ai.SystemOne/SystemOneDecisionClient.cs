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

        using HttpResponseMessage response = await httpClient
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        string responseBody = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw SystemOneErrorMapper.Create(response.StatusCode, responseBody);
        }

        SystemOneResponse mapped = SystemOneResponseMapper.Map(responseBody, request);
        DecisionResult parsed = DecisionResponseParser.Parse(
            mapped.ResultsJson,
            request,
            modelId,
            "native"
        );
        return parsed with { Usage = mapped.Usage };
    }

    public object? GetService(Type serviceType) =>
        serviceType.IsInstanceOfType(this) ? this : null;
}
