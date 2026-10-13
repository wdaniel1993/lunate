using System.Reflection;
using System.Runtime.Loader;

namespace Lunate.Extensibility;

/// <summary>
/// The collectible load context of one extension. The contract assemblies are shared with the
/// default context so contract types keep one identity across the boundary — including
/// <c>Lunate.Agent</c>, whose <c>ToolContext</c>/<c>ToolResult</c> the contract references; every
/// other dependency is isolated to the extension's own directory.
/// </summary>
public sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lunate.Extensibility.Abstractions",
        "Lunate.Agent",
        "Microsoft.Extensions.AI.Abstractions",
        "System.Text.Json",
    };

    private readonly string _directory;

    public ExtensionLoadContext(string extensionId, string directory)
        : base($"extension:{extensionId}", isCollectible: true)
    {
        _directory = directory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        string candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}
