namespace Lunate.Agent;

/// <summary>Generates the ids of one run: one run id and one id per streamed assistant message.</summary>
internal static class RunIds
{
    internal static string Next() => "run_" + Guid.NewGuid().ToString("N");

    internal static string NextMessage() => "msg_" + Guid.NewGuid().ToString("N");
}
