using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    /// <summary>
    /// Runs <see cref="IRunStartingHandler"/> handlers in order; each handler sees the sections the
    /// previous handlers produced. Over-budget added sections are dropped and logged.
    /// </summary>
    public async ValueTask<RunStartingDispatch> RunRunStartingAsync(
        RunStartingPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        _continuations.Remove(payload.RunId);
        var sections = new List<PromptSection>(payload.Sections);
        var edits = new List<PromptSectionEdit>();
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<string>? activeTools = null;

        foreach (Registration registration in Ordered<IRunStartingHandler>())
        {
            var handler = (IRunStartingHandler)registration.Handler;
            HandlerOutcome<RunStartingResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Sections = [.. sections] }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("RunStarting", registration, _options.HandlerTimeout, outcome.Error);
                continue;
            }

            if (outcome.Value is not RunStartingResult.Apply apply)
            {
                continue;
            }

            foreach (PromptSectionEdit edit in apply.SectionEdits)
            {
                bool isAddition = sections.All(section =>
                    !string.Equals(section.Name, edit.Name, StringComparison.Ordinal)
                );
                int cost = EstimateTokens(edit.Text ?? string.Empty);
                if (isAddition && IsOverBudget(registration, used, cost))
                {
                    LogBudgetDrop("RunStarting", registration, cost);
                    continue;
                }

                if (isAddition)
                {
                    used[registration.ExtensionId] =
                        used.GetValueOrDefault(registration.ExtensionId) + cost;
                }

                var stamped = edit with { Source = registration.ExtensionId };
                ApplyEdit(sections, stamped);
                edits.Add(stamped);
            }

            if (apply.ActiveTools is not null)
            {
                activeTools = apply.ActiveTools;
            }
        }

        return new RunStartingDispatch(edits, activeTools);
    }

    /// <summary>
    /// Runs <see cref="IContextBuildingHandler"/> handlers in order; each handler sees the previous
    /// additions. Over-budget additions are dropped and logged.
    /// </summary>
    public async ValueTask<ContextBuildingDispatch> RunContextBuildingAsync(
        ContextBuildingPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        var working = new List<ContextMessage>(payload.Messages);
        var additions = new List<ContextMessage>();
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Registration registration in Ordered<IContextBuildingHandler>())
        {
            var handler = (IContextBuildingHandler)registration.Handler;
            HandlerOutcome<ContextBuildingResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Messages = [.. working] }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("ContextBuilding", registration, _options.HandlerTimeout, outcome.Error);
                continue;
            }

            foreach (ContextMessage message in outcome.Value!.AddedMessages)
            {
                int cost = EstimateTokens(message.Text);
                if (IsOverBudget(registration, used, cost))
                {
                    LogBudgetDrop("ContextBuilding", registration, cost);
                    continue;
                }

                used[registration.ExtensionId] =
                    used.GetValueOrDefault(registration.ExtensionId) + cost;
                ContextMessage stamped = message with { Source = registration.ExtensionId };
                additions.Add(stamped);
                working.Add(stamped);
            }
        }

        return new ContextBuildingDispatch(additions);
    }

    private bool IsOverBudget(Registration registration, Dictionary<string, int> used, int cost) =>
        used.GetValueOrDefault(registration.ExtensionId) + cost
        > _options.ContextBudgetPerExtension;

    private static void ApplyEdit(List<PromptSection> sections, PromptSectionEdit edit)
    {
        int index = sections.FindIndex(section =>
            string.Equals(section.Name, edit.Name, StringComparison.Ordinal)
        );
        if (edit.Text is null)
        {
            if (index >= 0)
            {
                sections.RemoveAt(index);
            }

            return;
        }

        if (index >= 0)
        {
            sections[index] = new PromptSection(edit.Name, edit.Text);
        }
        else
        {
            sections.Add(new PromptSection(edit.Name, edit.Text));
        }
    }
}
