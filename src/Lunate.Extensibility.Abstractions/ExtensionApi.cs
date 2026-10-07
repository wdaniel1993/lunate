using System.Globalization;

namespace Lunate.Extensibility.Abstractions;

public static class ExtensionApi
{
    public const string Current = "1.2.0";

    public static bool IsCompatible(string range)
    {
        ArgumentNullException.ThrowIfNull(range);

        bool caret = range.StartsWith('^');
        (int major, int minor, int patch) = ParseVersion(caret ? range[1..] : range, range);
        (int currentMajor, int currentMinor, int currentPatch) = ParseVersion(Current, Current);
        bool atLeast = IsAtLeast(currentMajor, currentMinor, currentPatch, major, minor, patch);

        return caret
            ? atLeast && currentMajor == major
            : atLeast && currentMajor == major && currentMinor == minor && currentPatch == patch;
    }

    private static bool IsAtLeast(
        int major,
        int minor,
        int patch,
        int otherMajor,
        int otherMinor,
        int otherPatch
    ) =>
        major > otherMajor
        || (
            major == otherMajor
            && (minor > otherMinor || (minor == otherMinor && patch >= otherPatch))
        );

    private static (int Major, int Minor, int Patch) ParseVersion(string version, string range)
    {
        string[] parts = version.Split('.');
        if (
            parts.Length != 3
            || !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int major
            )
            || !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minor
            )
            || !int.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int patch
            )
        )
        {
            throw new InvalidDataException($"unsupported apiVersion range '{range}'");
        }

        return (major, minor, patch);
    }
}
