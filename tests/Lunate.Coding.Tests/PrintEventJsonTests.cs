using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class PrintEventJsonTests
{
    public static TheoryData<AgentEvent, string> GoldenLines =>
        new()
        {
            { new RunStarted("run_1"), """{"type":"run_started","runId":"run_1"}""" },
            {
                new RunFinished("run_1", StopReasons.StepLimit),
                """{"type":"run_finished","runId":"run_1","stopReason":"step_limit"}"""
            },
            {
                new RunError("run_1", "boom"),
                """{"type":"run_error","runId":"run_1","message":"boom"}"""
            },
            {
                new TextMessageStart("run_1", "msg_1"),
                """{"type":"text_message_start","runId":"run_1","messageId":"msg_1"}"""
            },
            {
                new TextMessageContent("run_1", "msg_1", "hi"),
                """{"type":"text_message_content","runId":"run_1","messageId":"msg_1","text":"hi"}"""
            },
            {
                new TextMessageEnd("run_1", "msg_1"),
                """{"type":"text_message_end","runId":"run_1","messageId":"msg_1"}"""
            },
            {
                new ReasoningStart("run_1", "msg_1"),
                """{"type":"reasoning_start","runId":"run_1","messageId":"msg_1"}"""
            },
            {
                new ReasoningMessageContent("run_1", "msg_1", "why"),
                """{"type":"reasoning_message_content","runId":"run_1","messageId":"msg_1","text":"why"}"""
            },
            {
                new ReasoningEnd("run_1", "msg_1"),
                """{"type":"reasoning_end","runId":"run_1","messageId":"msg_1"}"""
            },
            {
                new ToolCallStart("run_1", "call_1", "read"),
                """{"type":"tool_call_start","runId":"run_1","callId":"call_1","toolName":"read"}"""
            },
            {
                new ToolCallStart("run_1", "call_2", "read") { ParentToolCallId = "call_1" },
                """{"type":"tool_call_start","runId":"run_1","callId":"call_2","parentToolCallId":"call_1","toolName":"read"}"""
            },
            {
                new ToolCallArgs("run_1", "call_1", """{"path":"a.cs"}"""),
                """{"type":"tool_call_args","runId":"run_1","callId":"call_1","args":"{\u0022path\u0022:\u0022a.cs\u0022}"}"""
            },
            {
                new ToolCallEnd("run_1", "call_1"),
                """{"type":"tool_call_end","runId":"run_1","callId":"call_1"}"""
            },
            {
                new ToolCallResult(
                    "run_1",
                    "call_1",
                    "edited a.cs",
                    IsError: false,
                    new EditDetails("a.cs", 1, 2, "exact", "@@")
                ),
                """{"type":"tool_call_result","runId":"run_1","callId":"call_1","output":"edited a.cs","isError":false,"details":{"path":"a.cs","firstLine":1,"lastLine":2,"matchTier":"exact","diff":"@@"}}"""
            },
            {
                new ToolCallResult("run_1", "call_1", "no such file", IsError: true),
                """{"type":"tool_call_result","runId":"run_1","callId":"call_1","output":"no such file","isError":true}"""
            },
            {
                new ApprovalRequested("run_1", "call_1", "bash", """{"command":"ls"}"""),
                """{"type":"approval_requested","runId":"run_1","callId":"call_1","toolName":"bash","args":"{\u0022command\u0022:\u0022ls\u0022}"}"""
            },
            {
                new UsageUpdated(
                    "run_1",
                    new UsageDetails
                    {
                        InputTokenCount = 11,
                        OutputTokenCount = 5,
                        TotalTokenCount = 16,
                    }
                ),
                """{"type":"usage_updated","runId":"run_1","usage":{"inputTokenCount":11,"outputTokenCount":5,"totalTokenCount":16}}"""
            },
            {
                new Retrying("run_1", 2, "transient"),
                """{"type":"retrying","runId":"run_1","attempt":2,"reason":"transient"}"""
            },
            {
                new CompactionApplied("run_1", ["e_01", "e_02"], 123),
                """{"type":"compaction_applied","runId":"run_1","replacedEntryIds":["e_01","e_02"],"estimatedTokensAfter":123}"""
            },
            {
                new StepLimitReached("run_1", 50),
                """{"type":"step_limit_reached","runId":"run_1","maxSteps":50}"""
            },
            {
                new ToolProgressUpdate("run_1", "call_1", "working"),
                """{"type":"tool_progress_update","runId":"run_1","callId":"call_1","message":"working"}"""
            },
        };

    [Theory]
    [MemberData(nameof(GoldenLines))]
    public void Every_event_type_serializes_to_its_golden_line(
        AgentEvent agentEvent,
        string expected
    )
    {
        Assert.Equal(expected, PrintEventJson.Serialize(agentEvent));
    }

    [Fact]
    public void Usage_with_provider_cache_counts_adds_read_and_write_fields_next_to_the_unchanged_usage_object()
    {
        var usage = new UsageDetails
        {
            InputTokenCount = 1500,
            OutputTokenCount = 50,
            TotalTokenCount = 1550,
            CachedInputTokenCount = 1200,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = 300,
            },
        };

        Assert.Equal(
            """{"type":"usage_updated","runId":"run_1","usage":{"inputTokenCount":1500,"outputTokenCount":50,"totalTokenCount":1550,"cachedInputTokenCount":1200,"additionalCounts":{"CacheCreationInputTokens":300}},"cacheReadTokens":1200,"cacheWriteTokens":300}""",
            PrintEventJson.Serialize(new UsageUpdated("run_1", usage))
        );
    }

    [Theory]
    [InlineData(1200L, null)]
    [InlineData(null, 300L)]
    [InlineData(0L, null)]
    [InlineData(null, 0L)]
    [InlineData(0L, 0L)]
    public void Usage_omits_a_cache_count_the_provider_did_not_report(
        long? cacheRead,
        long? cacheWrite
    )
    {
        var usage = new UsageDetails
        {
            InputTokenCount = 1500,
            OutputTokenCount = 50,
            TotalTokenCount = 1550,
            CachedInputTokenCount = cacheRead,
        };
        if (cacheWrite is { } write)
        {
            usage.AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = write,
            };
        }

        using JsonDocument line = JsonDocument.Parse(
            PrintEventJson.Serialize(new UsageUpdated("run_1", usage))
        );
        JsonElement root = line.RootElement;

        Assert.Equal(cacheRead is > 0, root.TryGetProperty("cacheReadTokens", out _));
        Assert.Equal(cacheWrite is > 0, root.TryGetProperty("cacheWriteTokens", out _));
        if (cacheRead is > 0)
        {
            Assert.Equal(cacheRead.Value, root.GetProperty("cacheReadTokens").GetInt64());
        }

        if (cacheWrite is > 0)
        {
            Assert.Equal(cacheWrite.Value, root.GetProperty("cacheWriteTokens").GetInt64());
        }

        if (cacheRead is 0)
        {
            Assert.Equal(
                0,
                root.GetProperty("usage").GetProperty("cachedInputTokenCount").GetInt64()
            );
        }
    }

    [Fact]
    public void Every_concrete_event_type_is_handled()
    {
        string[] concrete =
        [
            .. typeof(AgentEvent)
                .Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(AgentEvent)) && !type.IsAbstract)
                .Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(
            concrete,
            PrintEventJson
                .HandledEventTypes.Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
        );
    }

    [Fact]
    public void Stamped_fields_lead_the_type_specific_fields()
    {
        var agentEvent = new RunFinished("run_1", StopReasons.Stop)
        {
            SessionId = "s_1",
            ParentRunId = "run_0",
            Source = "ext/foo",
        };

        Assert.Equal(
            """{"type":"run_finished","runId":"run_1","sessionId":"s_1","parentRunId":"run_0","source":"ext/foo","stopReason":"stop"}""",
            PrintEventJson.Serialize(agentEvent)
        );
    }

    [Fact]
    public void Details_serialize_the_runtime_type()
    {
        var result = new ToolCallResult(
            "run_1",
            "call_1",
            "wrote a.cs",
            IsError: false,
            new WriteDetails("a.cs", Created: true, Lines: 1, Diff: "@@")
        );

        Assert.Contains(
            "\"details\":{\"path\":\"a.cs\",\"created\":true,\"lines\":1,\"diff\":\"@@\"}",
            PrintEventJson.Serialize(result),
            StringComparison.Ordinal
        );
    }
}
