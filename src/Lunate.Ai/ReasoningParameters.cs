using System.Globalization;
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
    private const string InlineStyle = "inline";
    private const string OffDescription = "no reasoning parameters";

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
                ApplyBudget(options, model.WireId ?? model.Id, level);
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

    private static long BudgetFor(ThinkingLevel level) =>
        level switch
        {
            ThinkingLevel.Low => 2_048,
            ThinkingLevel.Medium => 8_192,
            _ => 32_768,
        };

    /// <summary>
    /// The active model's mapping for the bare <c>/thinking</c> notice: the effort an effort model
    /// would send, the token budget a budget model would send, the inline convention, or that the
    /// model has no reasoning support. <see cref="ThinkingLevel.Off"/> and a model without a
    /// declared style read as sending nothing. Numbers are formatted culture-invariantly.
    /// </summary>
    public static string Describe(ModelInfo model, ThinkingLevel level)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Reasoning switch
        {
            EffortStyle when level != ThinkingLevel.Off => "reasoning effort "
                + EffortFor(level).ToString().ToLowerInvariant(),
            BudgetStyle when level != ThinkingLevel.Off => "thinking budget "
                + BudgetFor(level).ToString(CultureInfo.InvariantCulture)
                + " tokens",
            EffortStyle or BudgetStyle => OffDescription,
            InlineStyle => "inline thinking (no provider parameter)",
            _ => "no reasoning support",
        };
    }

    /// <summary>
    /// Enables Anthropic extended thinking with the level's token budget. The request names the
    /// wire id, so a catalog id with a differing <see cref="ModelInfo.WireId"/> still reaches the
    /// provider. It carries an explicit <c>max_tokens</c> (an API requirement when thinking is on)
    /// clamped above the budget: an existing limit is raised when it leaves no room, otherwise it
    /// is kept.
    /// </summary>
    private static void ApplyBudget(ChatOptions options, string wireId, ThinkingLevel level)
    {
        long budget = BudgetFor(level);
        long maxTokens = Math.Max(options.MaxOutputTokens ?? 0, budget + OutputHeadroom);
        options.MaxOutputTokens = (int)maxTokens;
        options.RawRepresentationFactory = _ => CreateRequest(wireId, maxTokens, budget);
    }

    private static MessageCreateParams CreateRequest(string wireId, long maxTokens, long budget) =>
        new()
        {
            Model = wireId,
            MaxTokens = maxTokens,
            Messages = [],
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budget)),
        };
}
