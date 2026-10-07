namespace Lunate.Extensibility.Abstractions;

public interface IExtensionLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}
