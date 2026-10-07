using Lunate.Agent;

namespace Lunate.Agent.Tests;

public sealed class FileChangeSinkTests
{
    [Fact]
    public void The_default_sink_is_a_no_op()
    {
        var options = new AgentHarnessOptions();

        options.FileChanges.Notify("/anywhere/a.txt");
    }
}
