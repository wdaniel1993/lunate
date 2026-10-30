using System.Text.Json;
using Lunate.Agent;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols;

/// <summary>Maps MCP form requests to the agent's shared question interaction.</summary>
internal static partial class McpElicitationHandler
{
    internal static async ValueTask<ElicitResult> HandleAsync(
        ElicitRequestParams? request,
        string serverName,
        McpElicitationOutcome outcome,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return Result("decline");
        }

        if (!string.Equals(request.Mode, "form", StringComparison.Ordinal))
        {
            throw new McpProtocolException(
                "URL mode is not supported by Lunate; send a form elicitation with mode 'form' and requestedSchema. No URL was opened.",
                McpErrorCode.InvalidParams
            );
        }

        McpElicitationOutcome.InteractionLease? interaction = outcome.CurrentInteraction;
        if (interaction is null)
        {
            outcome.RecordUnavailable();
            return Result("cancel");
        }

        if (!TryMapRequest(request, serverName, out List<MappedQuestion>? mapped))
        {
            return Result("decline");
        }

        int exchangeId = interaction.BeginElicitation();
        List<MappedQuestion> questions = mapped!;
        if (interaction.Interaction is not { } askUser)
        {
            outcome.RecordUnavailable();
            interaction.CompleteElicitation(
                exchangeId,
                Exchange(serverName, request.Message, "unanswered: no user present", questions)
            );
            return Result("cancel");
        }

        UserQuestionResult answer = await askUser(
                questions.Select(question => question.Question).ToArray(),
                ct
            )
            .ConfigureAwait(false);
        if (answer.Status == UserQuestionStatus.Unavailable)
        {
            outcome.RecordUnavailable();
            interaction.CompleteElicitation(
                exchangeId,
                Exchange(serverName, request.Message, "unanswered: no user present", questions)
            );
            return Result("cancel");
        }

        if (answer.Status != UserQuestionStatus.Answered)
        {
            interaction.CompleteElicitation(
                exchangeId,
                Exchange(serverName, request.Message, "cancelled by user", questions)
            );
            return Result("cancel");
        }

        if (
            !TryBuildContent(
                questions,
                answer.Answers,
                out Dictionary<string, JsonElement>? content
            )
        )
        {
            interaction.CompleteElicitation(
                exchangeId,
                Exchange(
                    serverName,
                    request.Message,
                    "declined: answer did not match the requested schema",
                    questions
                )
            );
            return Result("decline");
        }

        interaction.CompleteElicitation(
            exchangeId,
            Exchange(
                serverName,
                request.Message,
                "Answer: " + JsonSerializer.Serialize(content),
                questions
            )
        );
        return new ElicitResult { Action = "accept", Content = content };
    }

    private static string Exchange(
        string serverName,
        string message,
        string answer,
        IReadOnlyList<MappedQuestion> questions
    ) =>
        $"MCP elicitation from server '{serverName}'\nQuestion: {message}\nFields:\n{string.Join("\n", questions.Select(question => $"- {question.Question.Header}{(question.Description.Length == 0 ? string.Empty : ": " + question.Description)}"))}\n{answer}";

    private static bool TryMapRequest(
        ElicitRequestParams request,
        string serverName,
        out List<MappedQuestion>? mapped
    )
    {
        mapped = null;
        ElicitRequestParams.RequestSchema? schema = request.RequestedSchema;
        if (
            !string.Equals(request.Mode, "form", StringComparison.Ordinal)
            || schema is not { Properties.Count: > 0 }
            || schema.Properties.Keys.Any(string.IsNullOrWhiteSpace)
        )
        {
            return false;
        }

        HashSet<string> required = new(schema.Required ?? [], StringComparer.Ordinal);
        if (required.Any(name => !schema.Properties.ContainsKey(name)))
        {
            return false;
        }

        List<MappedQuestion> questions = [];
        foreach (
            (
                string name,
                ElicitRequestParams.PrimitiveSchemaDefinition property
            ) in schema.Properties
        )
        {
            if (
                !TryMapField(
                    name,
                    property,
                    request.Message,
                    serverName,
                    required,
                    out MappedQuestion? question
                )
            )
            {
                return false;
            }

            questions.Add(question!);
        }

        mapped = questions;
        return true;
    }

    private static bool TryMapField(
        string name,
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        string message,
        string serverName,
        IReadOnlySet<string> required,
        out MappedQuestion? mapped
    )
    {
        mapped = null;
        bool isArray =
            schema
            is ElicitRequestParams.UntitledMultiSelectEnumSchema
                or ElicitRequestParams.TitledMultiSelectEnumSchema;
        if (!TryGetOptions(schema, out List<MappedOption> options))
        {
            return false;
        }

        string type = schema.Type ?? string.Empty;
        if (
            isArray
                ? !string.Equals(type, "array", StringComparison.Ordinal) || options.Count == 0
                : type is not ("string" or "number" or "integer" or "boolean")
                    || (options.Count > 0 && type != "string")
        )
        {
            return false;
        }

        string title = string.IsNullOrWhiteSpace(schema.Title) ? name : schema.Title;
        string description = schema.Description ?? string.Empty;
        string question = string.IsNullOrWhiteSpace(description)
            ? message
            : message + Environment.NewLine + description;
        UserQuestion userQuestion = new(
            $"{serverName}: {title}",
            question,
            options.Select(option => option.Option).ToArray(),
            MultiSelect: isArray,
            AllowFreeText: options.Count == 0
        );
        mapped = new MappedQuestion(
            name,
            type,
            userQuestion,
            options,
            description,
            required.Contains(name),
            isArray
        );
        return true;
    }

    private static bool TryGetOptions(
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        out List<MappedOption> options
    )
    {
        options = schema switch
        {
            ElicitRequestParams.UntitledSingleSelectEnumSchema single =>
            [
                .. (single.Enum ?? []).Select(value => new MappedOption(
                    value,
                    value,
                    single.Description
                )),
            ],
            ElicitRequestParams.TitledSingleSelectEnumSchema single =>
            [
                .. (single.OneOf ?? []).Select(value => new MappedOption(
                    value.Const,
                    value.Title,
                    single.Description
                )),
            ],
            ElicitRequestParams.UntitledMultiSelectEnumSchema multi =>
            [
                .. (multi.Items?.Enum ?? []).Select(value => new MappedOption(
                    value,
                    value,
                    multi.Description
                )),
            ],
            ElicitRequestParams.TitledMultiSelectEnumSchema multi =>
            [
                .. (multi.Items?.AnyOf ?? []).Select(value => new MappedOption(
                    value.Const,
                    value.Title,
                    multi.Description
                )),
            ],
            _ => [],
        };

        return options.All(option =>
                !string.IsNullOrWhiteSpace(option.Value)
                && !string.IsNullOrWhiteSpace(option.Option.Label)
            )
            && options
                .Select(option => option.Option.Label)
                .Distinct(StringComparer.Ordinal)
                .Count() == options.Count;
    }

    private static ElicitResult Result(string action) => new() { Action = action };

    private sealed record MappedQuestion(
        string Name,
        string Type,
        UserQuestion Question,
        IReadOnlyList<MappedOption> Options,
        string Description,
        bool Required,
        bool IsArray
    );

    private sealed record MappedOption(string Value, string Label, string? Description)
    {
        public UserQuestionOption Option { get; } =
            new(Label, Description ?? string.Empty, Recommended: false);
    }
}
