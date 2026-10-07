using System.Diagnostics.CodeAnalysis;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class EmptyExtensionSecrets : IExtensionSecrets
{
    public bool TryGet(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }
}
