using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Spike.OwnLoop;

internal sealed class AccumulatedCall
{
    public required string CallId { get; init; }

    public required string Name { get; init; }

    public StringBuilder RawArguments { get; } = new();

    public IDictionary<string, object?>? CompleteArguments { get; set; }

    public string ArgumentsJson => this.CompleteArguments is not null
        ? JsonSerializer.Serialize(this.CompleteArguments)
        : this.RawArguments.ToString();

    public bool TryGetArguments(out JsonElement arguments, out string? error)
    {
        if (this.CompleteArguments is not null)
        {
            arguments = JsonSerializer.SerializeToElement(this.CompleteArguments);
            error = null;
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(this.RawArguments.ToString());
            arguments = document.RootElement.Clone();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            arguments = default;
            error = ex.Message;
            return false;
        }
    }
}

internal sealed class StreamAccumulator(int turn)
{
    private readonly Dictionary<string, AccumulatedCall> _calls = new(StringComparer.Ordinal);
    private readonly List<string> _callOrder = [];
    private readonly List<OwnLoopEvent> _events = [];
    private readonly StringBuilder _text = new();
    private string? _textMessageId;
    private bool _textStarted;

    public IReadOnlyList<OwnLoopEvent> Process(ChatResponseUpdate update)
    {
        this._events.Clear();

        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    if (!this._textStarted)
                    {
                        this._textStarted = true;
                        this._textMessageId = $"m{turn}";
                        this._events.Add(new TextMessageStart(this._textMessageId));
                    }

                    this._text.Append(text.Text);
                    this._events.Add(new TextMessageContent(this._textMessageId!, text.Text));
                    break;

                case FunctionCallContent call:
                    AccumulatedCall state = this.GetOrAdd(call);
                    if (call.Arguments is { Count: > 0 } arguments)
                    {
                        if (arguments.TryGetValue("$raw", out object? raw) && raw is string fragment)
                        {
                            state.RawArguments.Append(fragment);
                        }
                        else
                        {
                            state.CompleteArguments = arguments;
                        }
                    }

                    break;

                case UsageContent usage:
                    this._events.Add(new UsageUpdated(usage.Details?.InputTokenCount, usage.Details?.OutputTokenCount));
                    break;
            }
        }

        return this._events.ToList();
    }

    public IReadOnlyList<OwnLoopEvent> CompleteTurn()
    {
        if (this._textStarted)
        {
            this._textStarted = false;
            return [new TextMessageEnd(this._textMessageId!)];
        }

        return [];
    }

    public List<AccumulatedCall> CompleteCalls() =>
        this._callOrder.Select(id => this._calls[id]).ToList();

    public ChatMessage ToAssistantMessage()
    {
        List<AIContent> contents = [];
        if (this._text.Length > 0)
        {
            contents.Add(new TextContent(this._text.ToString()));
        }

        foreach (AccumulatedCall call in this.CompleteCalls())
        {
            contents.Add(new FunctionCallContent(
                call.CallId,
                call.Name,
                call.CompleteArguments ?? new Dictionary<string, object?> { ["$raw"] = call.RawArguments.ToString() }));
        }

        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private AccumulatedCall GetOrAdd(FunctionCallContent call)
    {
        if (!this._calls.TryGetValue(call.CallId, out AccumulatedCall? state))
        {
            state = new AccumulatedCall { CallId = call.CallId, Name = call.Name };
            this._calls[call.CallId] = state;
            this._callOrder.Add(call.CallId);
        }

        return state;
    }
}
