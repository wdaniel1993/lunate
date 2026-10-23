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

    /// <summary>
    /// The welcome header's form: the display version plus the short build hash. A binary built from a
    /// working tree without a hash keeps the plain display form, so a developer build does not advertise
    /// a commit it cannot name.
    /// </summary>
    internal static string Build { get; } = BuildForm(Informational);

    internal static string BuildForm(string informational)
    {
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return informational;
        }

        string hash = informational[(plus + 1)..].Split('.')[0];
        return hash.Length >= 7
            ? string.Concat(informational.AsSpan(0, plus), "+", hash.AsSpan(0, 7))
            : informational;
    }

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
