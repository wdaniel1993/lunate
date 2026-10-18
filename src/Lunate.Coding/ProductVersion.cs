using System.Reflection;

namespace Lunate.Coding;

/// <summary>
/// The product version, read once from the assembly's informational version: the build's own
/// <c>--version</c> form and the display form the welcome header shows.
/// </summary>
internal static class ProductVersion
{
    /// <summary>The informational version, exactly as <c>--version</c> prints it.</summary>
    internal static string Informational { get; } = Read();

    /// <summary>The display form: the informational version without its build metadata.</summary>
    internal static string Display { get; } = Informational.Split('+')[0];

    private static string Read()
    {
        var assembly = typeof(ProductVersion).Assembly;
        return assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
