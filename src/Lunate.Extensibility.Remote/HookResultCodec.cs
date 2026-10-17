using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>
/// The result half of the hook wire contract: union families travel as one object with an
/// <c>outcome</c> discriminator and the variant's fields; concrete result DTOs travel as
/// themselves. Both directions use these methods, so the host and the wire reference can never
/// drift. A malformed result is a call error (never a framing error): the connection survives and
/// the hook's failure policy applies.
/// </summary>
public static class HookResultCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<Type> ProjectTrustResultTypes { get; } =
    [typeof(ProjectTrustResult), typeof(ProjectTrustResult.Allow), typeof(ProjectTrustResult.Deny)];

    public static IReadOnlyList<Type> InputReceivedResultTypes { get; } =
    [
        typeof(InputReceivedResult),
        typeof(InputReceivedResult.PassThrough),
        typeof(InputReceivedResult.Transform),
        typeof(InputReceivedResult.Consume),
    ];

    public static IReadOnlyList<Type> RunStartingResultTypes { get; } =
    [typeof(RunStartingResult), typeof(RunStartingResult.None), typeof(RunStartingResult.Apply)];

    public static IReadOnlyList<Type> ContextBuildingResultTypes { get; } =
    [typeof(ContextBuildingResult)];

    public static IReadOnlyList<Type> MessageCompletedResultTypes { get; } =
    [
        typeof(MessageCompletedResult),
        typeof(MessageCompletedResult.Keep),
        typeof(MessageCompletedResult.Replace),
    ];

    public static IReadOnlyList<Type> ToolCallingResultTypes { get; } =
    [typeof(ToolCallingResult), typeof(ToolCallingResult.Proceed), typeof(ToolCallingResult.Block)];

    public static IReadOnlyList<Type> ToolResultReadyResultTypes { get; } =
    [typeof(ToolResultReadyResult)];

    public static IReadOnlyList<Type> TurnEndedResultTypes { get; } =
    [typeof(TurnEndedResult), typeof(TurnEndedResult.None), typeof(TurnEndedResult.TurnEnded)];

    public static IReadOnlyList<Type> CompactingResultTypes { get; } =
    [
        typeof(CompactingResult),
        typeof(CompactingResult.UseDefault),
        typeof(CompactingResult.Provide),
    ];

    /// <summary>The wire encoding of one hook's payload DTO.</summary>
    public static JsonElement EncodePayload(HookContract contract, object payload) =>
        JsonSerializer.SerializeToElement(payload, contract.PayloadType, Options);

    /// <summary>Decodes one hook payload; malformed JSON throws <see cref="RemoteCallException"/>.</summary>
    public static object DecodePayload(HookContract contract, JsonElement payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, contract.PayloadType, Options)
                ?? throw Malformed(contract, "the payload was null");
        }
        catch (JsonException exception)
        {
            throw Malformed(contract, exception.Message);
        }
    }

    /// <summary>Encodes one hook result by the contract's codec.</summary>
    public static JsonElement EncodeResult(HookContract contract, object result)
    {
        if (contract.EncodeResult is not { } encode)
        {
            throw new InvalidOperationException(
                $"hook '{contract.Name}' is an observer and carries no result."
            );
        }

        return encode(result);
    }

    /// <summary>Decodes one hook result by the contract's codec.</summary>
    public static object DecodeResult(HookContract contract, JsonElement result)
    {
        if (contract.DecodeResult is not { } decode)
        {
            throw new InvalidOperationException(
                $"hook '{contract.Name}' is an observer and carries no result."
            );
        }

        try
        {
            return decode(result);
        }
        catch (RemoteCallException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Malformed(contract, exception.Message);
        }
    }

    public static JsonElement EncodeProjectTrust(ProjectTrustResult result) =>
        result switch
        {
            ProjectTrustResult.Allow => Outcome("allow"),
            ProjectTrustResult.Deny deny => Outcome("deny", ("reason", deny.Reason)),
            _ => throw Unknown(result),
        };

    public static ProjectTrustResult DecodeProjectTrust(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "allow" => new ProjectTrustResult.Allow(),
            "deny" => new ProjectTrustResult.Deny(RequiredString(result, "reason")),
            var outcome => throw UnknownOutcome("ProjectTrust", outcome),
        };

    public static JsonElement EncodeInputReceived(InputReceivedResult result) =>
        result switch
        {
            InputReceivedResult.PassThrough => Outcome("passThrough"),
            InputReceivedResult.Transform transform => Outcome(
                "transform",
                ("text", transform.Text)
            ),
            InputReceivedResult.Consume => Outcome("consume"),
            _ => throw Unknown(result),
        };

    public static InputReceivedResult DecodeInputReceived(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "passThrough" => new InputReceivedResult.PassThrough(),
            "transform" => new InputReceivedResult.Transform(RequiredString(result, "text")),
            "consume" => new InputReceivedResult.Consume(),
            var outcome => throw UnknownOutcome("InputReceived", outcome),
        };

    public static JsonElement EncodeRunStarting(RunStartingResult result) =>
        result switch
        {
            RunStartingResult.None => Outcome("none"),
            RunStartingResult.Apply apply => Outcome(
                "apply",
                ("sectionEdits", JsonSerializer.SerializeToElement(apply.SectionEdits, Options)),
                (
                    "activeTools",
                    apply.ActiveTools is null
                        ? null
                        : JsonSerializer.SerializeToElement(apply.ActiveTools, Options)
                )
            ),
            _ => throw Unknown(result),
        };

    public static RunStartingResult DecodeRunStarting(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "none" => new RunStartingResult.None(),
            "apply" => new RunStartingResult.Apply(
                Deserialize<IReadOnlyList<PromptSectionEdit>>(result, "sectionEdits"),
                Optional<IReadOnlyList<string>>(result, "activeTools")
            ),
            var outcome => throw UnknownOutcome("RunStarting", outcome),
        };

    public static JsonElement EncodeContextBuilding(ContextBuildingResult result) =>
        JsonSerializer.SerializeToElement(result, Options);

    public static ContextBuildingResult DecodeContextBuilding(JsonElement result) =>
        JsonSerializer.Deserialize<ContextBuildingResult>(result, Options)
        ?? throw MalformedText("ContextBuilding", "the result was null");

    public static JsonElement EncodeMessageCompleted(MessageCompletedResult result) =>
        result switch
        {
            MessageCompletedResult.Keep => Outcome("keep"),
            MessageCompletedResult.Replace replace => Outcome("replace", ("text", replace.Text)),
            _ => throw Unknown(result),
        };

    public static MessageCompletedResult DecodeMessageCompleted(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "keep" => new MessageCompletedResult.Keep(),
            "replace" => new MessageCompletedResult.Replace(RequiredString(result, "text")),
            var outcome => throw UnknownOutcome("MessageCompleted", outcome),
        };

    public static JsonElement EncodeToolCalling(ToolCallingResult result) =>
        result switch
        {
            ToolCallingResult.Proceed proceed => Outcome(
                "proceed",
                (
                    "arguments",
                    proceed.Arguments is { } arguments
                        ? JsonSerializer.SerializeToElement(arguments, Options)
                        : null
                )
            ),
            ToolCallingResult.Block block => Outcome("block", ("reason", block.Reason)),
            _ => throw Unknown(result),
        };

    public static ToolCallingResult DecodeToolCalling(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "proceed" => new ToolCallingResult.Proceed(OptionalElement(result, "arguments")),
            "block" => new ToolCallingResult.Block(RequiredString(result, "reason")),
            var outcome => throw UnknownOutcome("ToolCalling", outcome),
        };

    public static JsonElement EncodeToolResultReady(ToolResultReadyResult result) =>
        JsonSerializer.SerializeToElement(result, Options);

    public static ToolResultReadyResult DecodeToolResultReady(JsonElement result) =>
        JsonSerializer.Deserialize<ToolResultReadyResult>(result, Options)
        ?? throw MalformedText("ToolResultReady", "the result was null");

    public static JsonElement EncodeTurnEnded(TurnEndedResult result) =>
        result switch
        {
            TurnEndedResult.None => Outcome("none"),
            TurnEndedResult.TurnEnded ended => Outcome(
                "turnEnded",
                ("entries", JsonSerializer.SerializeToElement(ended.Entries, Options)),
                ("requestContinuation", ended.RequestContinuation)
            ),
            _ => throw Unknown(result),
        };

    public static TurnEndedResult DecodeTurnEnded(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "none" => new TurnEndedResult.None(),
            "turnEnded" => new TurnEndedResult.TurnEnded(
                Deserialize<IReadOnlyList<TurnEndedEntry>>(result, "entries"),
                RequiredBool(result, "requestContinuation")
            ),
            var outcome => throw UnknownOutcome("TurnEnded", outcome),
        };

    public static JsonElement EncodeCompacting(CompactingResult result) =>
        result switch
        {
            CompactingResult.UseDefault => Outcome("useDefault"),
            CompactingResult.Provide provide => Outcome("provide", ("summary", provide.Summary)),
            _ => throw Unknown(result),
        };

    public static CompactingResult DecodeCompacting(JsonElement result) =>
        OutcomeOf(result) switch
        {
            "useDefault" => new CompactingResult.UseDefault(),
            "provide" => new CompactingResult.Provide(RequiredString(result, "summary")),
            var outcome => throw UnknownOutcome("Compacting", outcome),
        };

    private static JsonElement Outcome(string outcome, params (string Name, object? Value)[] fields)
    {
        var node = new JsonObject { ["outcome"] = outcome };
        foreach ((string name, object? value) in fields)
        {
            node[name] = value switch
            {
                null => null,
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                bool boolean => JsonValue.Create(boolean),
                string text => JsonValue.Create(text),
                _ => JsonSerializer.SerializeToNode(value, value.GetType(), Options),
            };
        }

        return JsonSerializer.SerializeToElement(node, Options);
    }

    private static string OutcomeOf(JsonElement result)
    {
        if (
            result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("outcome", out JsonElement outcome)
            || outcome.ValueKind != JsonValueKind.String
        )
        {
            throw MalformedText(
                "the hook",
                $"a result object with a string 'outcome' is required: {Excerpt(result)}"
            );
        }

        return outcome.GetString()!;
    }

    private static string RequiredString(JsonElement result, string name) =>
        result.TryGetProperty(name, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw MalformedText("the hook", $"the '{name}' field must be a string.");

    private static bool RequiredBool(JsonElement result, string name) =>
        result.TryGetProperty(name, out JsonElement element)
        && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : throw MalformedText("the hook", $"the '{name}' field must be a boolean.");

    private static T Deserialize<T>(JsonElement result, string name)
        where T : notnull =>
        result.TryGetProperty(name, out JsonElement element)
        && element.ValueKind != JsonValueKind.Null
            ? JsonSerializer.Deserialize<T>(element, Options)
                ?? throw MalformedText("the hook", $"the '{name}' field was null.")
            : throw MalformedText("the hook", $"the '{name}' field is required.");

    private static T? Optional<T>(JsonElement result, string name)
        where T : class =>
        result.TryGetProperty(name, out JsonElement element)
        && element.ValueKind != JsonValueKind.Null
            ? JsonSerializer.Deserialize<T>(element, Options)
            : null;

    private static JsonElement? OptionalElement(JsonElement result, string name) =>
        result.TryGetProperty(name, out JsonElement element)
        && element.ValueKind != JsonValueKind.Null
            ? element.Clone()
            : null;

    private static RemoteCallException Unknown(object result) =>
        new(
            -32603,
            $"cannot encode {result.GetType().FullName} on the wire; it is not a catalogue result variant"
        );

    private static RemoteCallException UnknownOutcome(string hook, string outcome) =>
        MalformedText(hook, $"unknown result outcome '{outcome}'.");

    private static RemoteCallException Malformed(HookContract contract, string detail) =>
        MalformedText(contract.Name, detail);

    private static RemoteCallException MalformedText(string hook, string detail) =>
        new(-32603, $"the extension answered hook '{hook}' with a malformed result: {detail}");

    private static string Excerpt(JsonElement result) =>
        result.GetRawText() is { Length: > 120 } raw ? raw[..120] + "…" : result.GetRawText();
}
