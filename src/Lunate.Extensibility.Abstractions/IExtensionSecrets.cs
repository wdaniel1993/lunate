using System.Diagnostics.CodeAnalysis;

namespace Lunate.Extensibility.Abstractions;

public interface IExtensionSecrets
{
    bool TryGet(string name, [NotNullWhen(true)] out string? value);
}
