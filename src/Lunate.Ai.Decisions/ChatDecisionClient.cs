using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Ai.Decisions;

internal sealed class ChatDecisionClient(IChatClient chatClient) : IDecisionClient
{
    public async Task<DecisionResult> GetDecisionAsync(
        DecisionRequest request,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        DecisionRequestValidator.Validate(request);
        List<UsageDetails> usages = [];
        string? failure = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            ChatResponse response = await chatClient
                .GetResponseAsync(
                    CreateMessages(request),
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (response.Usage is { } usage)
            {
                usages.Add(usage);
            }

            try
            {
                DecisionResult result = DecisionResponseParser.Parse(
                    response.Text ?? string.Empty,
                    request,
                    response.ModelId ?? options?.ModelId ?? request.ModelId,
                    "adapter"
                );
                return result with { ChatUsageAttempts = usages };
            }
            catch (InvalidDataException exception)
            {
                failure = exception.Message;
            }
            catch (JsonException)
            {
                failure = "response is not valid JSON";
            }
        }

        throw new InvalidDataException(
            $"Decision for model '{options?.ModelId ?? request.ModelId}' failed after retry: {failure ?? "response did not match the decision contract"}."
        );
    }

    public object? GetService(Type serviceType) => chatClient.GetService(serviceType);

    private static IEnumerable<ChatMessage> CreateMessages(DecisionRequest request)
    {
        object payload = new
        {
            questions = request.Questions.Select(question => new
            {
                id = question.Id,
                text = question.Instructions,
                kind = question.Kind switch
                {
                    DecisionQuestionKind.Noul => "yes/no",
                    DecisionQuestionKind.Choice => "choice",
                    DecisionQuestionKind.Score => "score",
                    DecisionQuestionKind.Box => "box",
                    _ => throw new InvalidOperationException("The question kind is unsupported."),
                },
                answers = question.Answers,
                criteria = question.Criteria,
                state = question.State ?? request.State,
            }),
        };
        string userText = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return
        [
            new ChatMessage(
                ChatRole.System,
                "Evaluate every question independently. Return JSON with a 'results' object keyed by question id. Each value has an 'answer' string and a 'probabilities' object with one probability for every defined answer; probabilities must sum to 1. For a box question, return four fractions and confidence."
            ),
            new ChatMessage(ChatRole.User, userText),
        ];
    }
}
