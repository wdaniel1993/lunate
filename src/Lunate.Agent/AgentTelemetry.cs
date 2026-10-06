using System.Diagnostics;

namespace Lunate.Agent;

/// <summary>
/// The telemetry surface of the loop. Names follow the pinned OpenTelemetry GenAI conventions
/// (Development status; verified 2026-10-06 against the GenAI agent spans conventions): span names
/// <c>invoke_agent {gen_ai.agent.name}</c> and <c>execute_tool {gen_ai.tool.name}</c>, operations
/// <c>invoke_agent</c> and <c>execute_tool</c>, attributes <c>gen_ai.operation.name</c>,
/// <c>gen_ai.agent.name</c>, <c>gen_ai.tool.name</c> and <c>gen_ai.tool.call.id</c>.
/// <c>lunate.tool.is_error</c> is Lunate's own attribute.
/// </summary>
internal static class AgentTelemetry
{
    internal const string SourceName = "Lunate.Agent";
    internal const string InvokeAgentOperation = "invoke_agent";
    internal const string ExecuteToolOperation = "execute_tool";
    internal const string AgentName = "lunate";
    internal const string OperationNameAttribute = "gen_ai.operation.name";
    internal const string AgentNameAttribute = "gen_ai.agent.name";
    internal const string ToolNameAttribute = "gen_ai.tool.name";
    internal const string ToolCallIdAttribute = "gen_ai.tool.call.id";
    internal const string ToolIsErrorAttribute = "lunate.tool.is_error";

    internal static ActivitySource Source { get; } = new(SourceName);
}
