using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Spike.MafHarness;

public sealed record MappedEvent(string Name, string Detail)
{
    public string Line => $"{this.Name} {this.Detail}";
}

public sealed class HarnessEventMapper
{
    private readonly List<MappedEvent> _events = [];
    private string? _textMessageId;
    private bool _textStarted;
    private int _messageCounter;

    public IReadOnlyList<MappedEvent> Map(AgentResponseUpdate update)
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
                        this._textMessageId = update.MessageId ?? $"m{++this._messageCounter}";
                        this._events.Add(new MappedEvent("text_message_start", this._textMessageId));
                    }

                    this._events.Add(new MappedEvent("text_message_content", $"{this._textMessageId} {JsonSerializer.Serialize(text.Text)}"));
                    break;

                case FunctionCallContent call:
                    this.CloseTextMessage();
                    this._events.Add(new MappedEvent("tool_call_start", $"{call.CallId} {call.Name}"));
                    this._events.Add(new MappedEvent("tool_call_args", $"{call.CallId} {JsonSerializer.Serialize(call.Arguments)}"));
                    this._events.Add(new MappedEvent("tool_call_end", call.CallId ?? "-"));
                    break;

                case FunctionResultContent result:
                    this.CloseTextMessage();
                    this._events.Add(new MappedEvent("tool_call_result", $"{result.CallId} {JsonSerializer.Serialize(result.Result)}"));
                    break;

                case ToolApprovalRequestContent request when request.ToolCall is FunctionCallContent call:
                    this._events.Add(new MappedEvent("approval_requested", $"{call.CallId} {call.Name} {JsonSerializer.Serialize(call.Arguments)}"));
                    break;

                case ToolApprovalResponseContent response when response.ToolCall is FunctionCallContent call:
                    this._events.Add(new MappedEvent("approval_response", $"{call.CallId} approved={response.Approved.ToString().ToLowerInvariant()}"));
                    break;

                case UsageContent usage:
                    this._events.Add(new MappedEvent("usage_updated", $"in={usage.Details?.InputTokenCount?.ToString() ?? "-"} out={usage.Details?.OutputTokenCount?.ToString() ?? "-"}"));
                    break;

                case ErrorContent error:
                    this._events.Add(new MappedEvent("error", JsonSerializer.Serialize(error.Message)));
                    break;

                default:
                    this._events.Add(new MappedEvent("unmapped_content", content.GetType().Name));
                    break;
            }
        }

        return this._events.ToList();
    }

    public IReadOnlyList<MappedEvent> Complete()
    {
        if (this._textStarted)
        {
            this._textStarted = false;
            return [new MappedEvent("text_message_end", this._textMessageId ?? "m?")];
        }

        return [];
    }

    private void CloseTextMessage()
    {
        if (this._textStarted)
        {
            this._textStarted = false;
            this._events.Add(new MappedEvent("text_message_end", this._textMessageId ?? "m?"));
        }
    }
}
