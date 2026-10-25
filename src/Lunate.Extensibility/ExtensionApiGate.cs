using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The contract gate: an extension built against a newer API than this host implements is refused at
/// load, before any of its code runs, with the version it needs and the version it got. The evidence
/// is always the version the extension's build saw — the manifest's declared range for a packaged
/// declaration, the assembly reference a packaged DLL carries, and the <c>apiVersion</c> a
/// single-file build recorded in its <c>build.json</c> — never a presence test for a DLL and never
/// the first call into the extension.
/// </summary>
public static class ExtensionApiGate
{
    /// <summary>The API version this host implements.</summary>
    public static string Current => ExtensionApi.Current;

    /// <summary>
    /// The refusal for an extension whose build saw <paramref name="required"/>, or null when that
    /// version is at or below <see cref="Current"/> (a file compiled before an API bump still loads).
    /// </summary>
    public static string? Refuse(string extensionId, string required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(required);

        if (Parse(required) is not { } version)
        {
            return Declares(extensionId, required);
        }

        return Parse(Current) is { } current && version > current
            ? Requires(extensionId, required)
            : null;
    }

    /// <summary>
    /// The refusal for a packaged manifest's declared range, or null when
    /// <see cref="ExtensionApi.IsCompatible"/> accepts it. A range whose floor is above the host is
    /// reported as the version it requires; a range the host does not satisfy for the other reason
    /// (an exact form that is not this version) is reported as a declaration the host cannot meet,
    /// so the message never claims a requirement the extension did not make.
    /// </summary>
    public static string? RefuseRange(string extensionId, string range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(range);

        bool compatible;
        try
        {
            compatible = ExtensionApi.IsCompatible(range);
        }
        catch (InvalidDataException)
        {
            return Declares(extensionId, range);
        }

        if (compatible)
        {
            return null;
        }

        string required = range.StartsWith('^') ? range[1..] : range;
        return Parse(required) is { } version && Parse(Current) is { } current && version > current
            ? Requires(extensionId, required)
            : Declares(extensionId, range);
    }

    /// <summary>The exact wording the requirement names: the required version, then the running one.</summary>
    public static string Requires(string extensionId, string required) =>
        FormattableString.Invariant(
            $"extension '{extensionId}' requires Lunate ≥ {required} but this is Lunate {Current}; it was not loaded."
        );

    /// <summary>
    /// The version of <c>Lunate.Extensibility.Abstractions</c> a packaged assembly was compiled
    /// against, read from its metadata as <c>x.y.z</c>; null when the file has no such reference (or
    /// cannot be read), which is a packaging problem the caller reports rather than a version.
    /// </summary>
    public static string? ReferencedVersion(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata)
            {
                return null;
            }

            MetadataReader metadata = reader.GetMetadataReader();
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                if (
                    string.Equals(
                        metadata.GetString(reference.Name),
                        typeof(IExtensionFactory).Assembly.GetName().Name,
                        StringComparison.Ordinal
                    )
                )
                {
                    return FormattableString.Invariant(
                        $"{reference.Version.Major.ToString(CultureInfo.InvariantCulture)}.{reference.Version.Minor.ToString(CultureInfo.InvariantCulture)}.{reference.Version.Build.ToString(CultureInfo.InvariantCulture)}"
                    );
                }
            }

            return null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or BadImageFormatException
            )
        {
            return null;
        }
    }

    private static string Declares(string extensionId, string version) =>
        FormattableString.Invariant(
            $"extension '{extensionId}' declares apiVersion '{version}', which this Lunate ({Current}) does not satisfy; it was not loaded."
        );

    /// <summary>A three-part version, or null for anything a version cannot be read out of.</summary>
    private static Version? Parse(string text)
    {
        string[] parts = text.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        int[] numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (
                !int.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[index]
                )
            )
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }
}
