using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Lunate.Ai;

/// <summary>
/// Translates the session thinking level into provider parameters per the model's declared
/// <see cref="ModelInfo.Reasoning"/> style (design D3 of the reasoning-end-to-end change):
/// <c>effort</c> maps to the OpenAI-compatible reasoning effort, <c>budget</c> maps to Anthropic
/// extended thinking with an explicit budget, and <c>inline</c> sends nothing (the model embeds
/// the thinking text in its stream). <see cref="ThinkingLevel.Off"/> — and a model without a
/// declared style — sends no reasoning parameters.
/// </summary>
public static class ReasoningParameters
{
    private const string EffortStyle = "effort";
    private const string BudgetStyle = "budget";

    /// <summary>
    /// The output headroom kept above a thinking budget: <c>max_tokens</c> must be present and
    /// exceed the budget, so an explicit limit that leaves no room is raised by this much (the
    /// same headroom the Anthropic SDK reserves when it raises an absent limit itself).
    /// </summary>
    private const int OutputHeadroom = 1_024;

    public static void Apply(ChatOptions options, ModelInfo model, ThinkingLevel level)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(model);
        if (level == ThinkingLevel.Off)
        {
            return;
        }

        switch (model.Reasoning)
        {
            case EffortStyle:
                options.Reasoning = new() { Effort = EffortFor(level) };
                break;
            case BudgetStyle:
                ApplyBudget(options, model.Id, level);
                break;
        }
    }

    private static ReasoningEffort EffortFor(ThinkingLevel level) =>
        level switch
        {
            ThinkingLevel.Low => ReasoningEffort.Low,
            ThinkingLevel.Medium => ReasoningEffort.Medium,
            _ => ReasoningEffort.High,
        };

    /// <summary>
    /// Enables Anthropic extended thinking with the level's token budget. The request carries an
    /// explicit <c>max_tokens</c> (an API requirement when thinking is on) clamped above the
    /// budget: an existing limit is raised when it leaves no room, otherwise it is kept.
    /// </summary>
    private static void ApplyBudget(ChatOptions options, string modelId, ThinkingLevel level)
    {
        long budget = level switch
        {
            ThinkingLevel.Low => 2_048,
            ThinkingLevel.Medium => 8_192,
            _ => 32_768,
        };
        long maxTokens = Math.Max(options.MaxOutputTokens ?? 0, budget + OutputHeadroom);
        options.MaxOutputTokens = (int)maxTokens;
        options.RawRepresentationFactory = _ => CreateRequest(modelId, maxTokens, budget);
    }

    private static MessageCreateParams CreateRequest(string modelId, long maxTokens, long budget) =>
        new()
        {
            Model = modelId,
            MaxTokens = maxTokens,
            Messages = [],
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budget)),
        };
}
