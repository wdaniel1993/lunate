using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The host log of a production session: warnings and errors reach the mode's stderr writer with
/// the <c>lunate: </c> prefix; extension-internal info is suppressed. Writes are serialized because
/// extension background services log from their own threads.
/// </summary>
internal sealed class StderrExtensionLog : IExtensionLog
{
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    internal StderrExtensionLog(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public void Info(string message) { }

    public void Warn(string message) => Write(message);

    public void Error(string message) => Write(message);

    private void Write(string message)
    {
        lock (_gate)
        {
            _writer.WriteLine("lunate: " + message);
        }
    }
}
