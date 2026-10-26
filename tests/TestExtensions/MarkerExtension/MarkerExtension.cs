using System.Runtime.CompilerServices;
using Lunate.Extensibility.Abstractions;

namespace MarkerExtension;

/// <summary>
/// An extension whose code is observable from outside: loading the assembly runs
/// <see cref="LoadMarker"/>'s module initializer, and creating the extension runs the factory. Both
/// drop a file beside the assembly, so a test can prove that an install did neither — the marker is
/// absent exactly when no assembly was loaded and no factory ran.
/// </summary>
public sealed class MarkerExtensionFactory : IExtensionFactory
{
    public const string FactoryMarkerFileName = "factory-ran.marker";

    public const string LoadMarkerFileName = "assembly-loaded.marker";

    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Write(FactoryMarkerFileName, "the factory ran");
        return new MarkerExtension(context.Id);
    }

    /// <summary>Writes the marker beside the executing assembly; a missing directory is a no-op.</summary>
    internal static void Write(string fileName, string text)
    {
        string? directory = Path.GetDirectoryName(typeof(MarkerExtensionFactory).Assembly.Location);
        if (directory is { Length: > 0 })
        {
            File.WriteAllText(Path.Combine(directory, fileName), text);
        }
    }
}

/// <summary>The extension itself: nothing but its id, because what it does is being loaded.</summary>
public sealed class MarkerExtension(string id) : IExtension
{
    public string Id { get; } = id;
}

internal static class LoadMarker
{
    [ModuleInitializer]
    internal static void Initialize() =>
        MarkerExtensionFactory.Write(
            MarkerExtensionFactory.LoadMarkerFileName,
            "the assembly loaded"
        );
}
