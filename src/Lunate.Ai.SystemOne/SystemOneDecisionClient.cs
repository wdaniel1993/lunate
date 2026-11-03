using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Ai.SystemOne;

internal sealed class SystemOneDecisionClient(HttpClient httpClient, Uri endpoint) : IDecisionClient
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
        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync(
                endpoint,
                SystemOneRequestMapper.Create(request, modelId),
                JsonOptions,
                cancellationToken
            )
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
