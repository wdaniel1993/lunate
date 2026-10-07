namespace Lunate.Agent;

/// <summary>
/// The harness's file change seam: the host injects a sink (the extension file change bus) into
/// the file-mutating tools. The harness itself never publishes; a tool calls the sink once after a
/// successful mutation.
/// </summary>
public interface IFileChangeSink
{
    void Notify(string absolutePath);
}

/// <summary>The default sink: mutations emit nothing.</summary>
public sealed class NullFileChangeSink : IFileChangeSink
{
    private NullFileChangeSink() { }

    public static NullFileChangeSink Instance { get; } = new();

    public void Notify(string absolutePath) { }
}
