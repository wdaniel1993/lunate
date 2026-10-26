using System.Globalization;

namespace Lunate.TestSupport;

/// <summary>
/// The one repository-root locator. Tests read committed fixtures relative to the directory that
/// holds <c>lunate.slnx</c>; this walks up to it so no test project carries its own copy of the walk,
/// its own marker spelling, or its own message.
/// </summary>
public static class RepositoryRoot
{
    /// <summary>The solution file that marks the repository root.</summary>
    public const string Marker = "lunate.slnx";

    /// <summary>The repository root above the running test binary.</summary>
    public static string Find() => Find(AppContext.BaseDirectory);

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> and returns the first directory holding
    /// <c>lunate.slnx</c>.
    /// </summary>
    /// <param name="startDirectory">The directory the walk starts at.</param>
    /// <returns>The full path of the repository root.</returns>
    /// <exception cref="InvalidOperationException">
    /// Neither the start directory nor any parent holds the marker.
    /// </exception>
    public static string Find(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        for (
            DirectoryInfo? directory = new(startDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Could not find {Marker} in '{startDirectory}' or any of its parents."
            )
        );
    }
}
