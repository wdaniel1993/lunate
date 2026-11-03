using System.Net.Http.Json;
using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Ai.SystemOne;

internal sealed class SystemOneDecisionClient(HttpClient httpClient, SystemOnePreset preset)
    : IDecisionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        DecisionRequestValidator.Validate(request);

        string modelId = options?.ModelId ?? request.ModelId;
        using HttpRequestMessage message = new(HttpMethod.Post, preset.Endpoint)
        {
            Content = JsonContent.Create(
                SystemOneRequestMapper.Create(request, modelId),
                options: JsonOptions
            ),
        };
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
