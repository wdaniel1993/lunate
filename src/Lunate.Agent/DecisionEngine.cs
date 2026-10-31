using System.Text;
using System.Text.Json;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

internal static class DecisionEngine
{
    public static async Task<DecisionResponse> EvaluateAsync(
        DecisionRequest request,
        IChatClient client,
        string modelId,
        string method,
        CancellationToken cancellationToken = default,
        Action<UsageDetails, string, string?, double?>? recordUsage = null
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        IReadOnlyList<PreparedDecisionQuestion> questions = DecisionRequestValidator.Prepare(
            request
        );
        INativeDecisionClient? native =
            method == "native"
                ? client.GetService(typeof(INativeDecisionClient)) as INativeDecisionClient
                : null;
        bool useNative = method == "native" && native is not null;
        string resolvedMethod = useNative ? "native" : "adapter";
        string? failure = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string text;
            ChatResponse? chatResponse = null;
            try
            {
                text = useNative
                    ? await native!
                        .GetDecisionAsync(request, cancellationToken)
                        .ConfigureAwait(false)
                    : (
                        chatResponse = await GetAdapterResponseAsync(
                                request,
                                questions,
                                client,
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                    ).Text
                        ?? string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            try
            {
                string resolvedModelId = chatResponse?.ModelId ?? modelId;
                DecisionResponse result = ParseResponse(
                    text,
                    questions,
                    resolvedModelId,
                    resolvedMethod
                );
                if (chatResponse?.Usage is { } usage)
                {
                    recordUsage?.Invoke(
                        usage,
                        resolvedModelId,
                        resolvedMethod,
                        result.Results.Values.First().Probability
                    );
                }

                return result;
            }
            catch (InvalidDataException exception)
            {
                failure = exception.Message;
                if (chatResponse?.Usage is { } usage)
                {
                    recordUsage?.Invoke(usage, chatResponse.ModelId ?? modelId, null, null);
                }
            }
            catch (JsonException)
            {
                failure = "response is not valid JSON";
                if (chatResponse?.Usage is { } usage)
                {
                    recordUsage?.Invoke(usage, chatResponse.ModelId ?? modelId, null, null);
                }
            }
        }

        throw new InvalidDataException(
            $"Decision for model '{modelId}' failed after retry: {failure ?? "response did not match the decision contract"}."
        );
    }

    private static async Task<ChatResponse> GetAdapterResponseAsync(
        DecisionRequest request,
        IReadOnlyList<PreparedDecisionQuestion> questions,
        IChatClient client,
        CancellationToken cancellationToken
    )
    {
        object payload = new
        {
            questions = questions.Select(question => new
            {
                id = question.Question.Id,
                text = question.Question.Text,
                kind = question.Question.Kind switch
                {
                    DecisionQuestionKind.YesNo => "yes/no",
                    DecisionQuestionKind.Choice => "choice",
                    DecisionQuestionKind.Score => "score",
                    _ => throw new InvalidOperationException(
                        "The validated question kind is unsupported."
                    ),
                },
                answers = question.Answers,
                state = question.State,
            }),
        };
        string userText = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return await client
            .GetResponseAsync(
                [
                    new ChatMessage(
                        ChatRole.System,
                        "Evaluate every question independently. Return JSON with a 'results' object keyed by question id. Each value has an 'answer' string and a 'probabilities' object with one probability for every defined answer; probabilities must sum to 1."
                    ),
                    new ChatMessage(ChatRole.User, userText),
                ],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static DecisionResponse ParseResponse(
        string text,
        IReadOnlyList<PreparedDecisionQuestion> questions,
        string modelId,
        string method
    )
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("response must contain a keyed 'results' object");
        }

        Dictionary<string, DecisionAnswer> validated = new(StringComparer.Ordinal);
        foreach (JsonProperty result in results.EnumerateObject())
        {
            PreparedDecisionQuestion? question = questions.FirstOrDefault(candidate =>
                string.Equals(candidate.Question.Id, result.Name, StringComparison.Ordinal)
            );
            if (question is null || result.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"response contains an undefined question id '{result.Name}'"
                );
            }
            if (validated.ContainsKey(result.Name))
            {
                throw new InvalidDataException($"response repeats question id '{result.Name}'");
            }

            if (
                !result.Value.TryGetProperty("answer", out JsonElement answerElement)
                || answerElement.ValueKind != JsonValueKind.String
                || !result.Value.TryGetProperty("probabilities", out JsonElement probabilityElement)
                || probabilityElement.ValueKind != JsonValueKind.Object
            )
            {
                throw new InvalidDataException(
                    $"response for question '{result.Name}' must contain an answer and probabilities object"
                );
            }

            Dictionary<string, double> probabilities = new(StringComparer.Ordinal);
            foreach (JsonProperty probability in probabilityElement.EnumerateObject())
            {
                if (
                    !probability.Value.TryGetDouble(out double value)
                    || !probabilities.TryAdd(probability.Name, value)
                )
                {
                    throw new InvalidDataException(
                        $"response for question '{result.Name}' has an invalid probability"
                    );
                }
            }

            validated.Add(
                result.Name,
                DecisionRequestValidator.ValidateResult(
                    question,
                    answerElement.GetString(),
                    probabilities,
                    modelId,
                    method
                )
            );
        }

        if (validated.Count != questions.Count)
        {
            string missing = string.Join(
                ", ",
                questions
                    .Where(question => !validated.ContainsKey(question.Question.Id))
                    .Select(question => question.Question.Id)
            );
            throw new InvalidDataException($"response is missing question results: {missing}");
        }

        return new DecisionResponse(validated);
    }
}
