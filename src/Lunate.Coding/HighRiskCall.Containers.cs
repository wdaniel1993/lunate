using System.Text.Json;

namespace Lunate.Coding;

internal static partial class HighRiskCall
{
    private static bool MountsStateRoot(
        JsonElement args,
        string workspaceRoot,
        string storeRoot,
        string homeRoot
    )
    {
        if (
            !args.TryGetProperty("command", out JsonElement element)
            || element.ValueKind is not JsonValueKind.String
        )
        {
            return false;
        }

        IReadOnlyList<string> words = Words(element.GetString() ?? string.Empty);
        for (int index = 0; index + 1 < words.Count; index++)
        {
            if (!IsContainerEngine(words[index]) || words[index + 1] != "run")
            {
                continue;
            }

            for (int option = index + 2; option < words.Count; option++)
            {
                string argument = words[option];
                string? mount = null;
                if (argument is "-v" or "--volume" or "--mount")
                {
                    if (option + 1 < words.Count)
                    {
                        mount = words[++option];
                    }
                }
                else if (argument.StartsWith("--volume=", StringComparison.Ordinal))
                {
                    mount = argument["--volume=".Length..];
                }
                else if (argument.StartsWith("--mount=", StringComparison.Ordinal))
                {
                    mount = argument["--mount=".Length..];
                }

                string? source =
                    argument is "--mount"
                    || argument.StartsWith("--mount=", StringComparison.Ordinal)
                        ? BindMountSource(mount)
                        : VolumeSource(mount);
                if (
                    source is not null
                    && ResolvesInsideStore(source, workspaceRoot, storeRoot, homeRoot)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? VolumeSource(string? mount)
    {
        if (mount is null)
        {
            return null;
        }

        int separator = mount.IndexOf(':');
        if (
            separator == 1
            && mount.Length > 2
            && char.IsAsciiLetter(mount[0])
            && mount[2] is '/' or '\\'
        )
        {
            separator = mount.IndexOf(':', 2);
        }

        return separator > 0 ? mount[..separator] : null;
    }

    private static string? BindMountSource(string? mount)
    {
        if (mount is null)
        {
            return null;
        }

        bool isBind = false;
        string? source = null;
        foreach (string field in mount.Split(',', StringSplitOptions.TrimEntries))
        {
            if (field.Equals("type=bind", StringComparison.OrdinalIgnoreCase))
            {
                isBind = true;
            }
            else if (
                field.StartsWith("source=", StringComparison.OrdinalIgnoreCase)
                || field.StartsWith("src=", StringComparison.OrdinalIgnoreCase)
            )
            {
                source = field[(field.IndexOf('=') + 1)..];
            }
        }

        return isBind ? source : null;
    }

    private static bool IsContainerEngine(string word)
    {
        string name = Path.GetFileName(word);
        return name.Equals("docker", StringComparison.OrdinalIgnoreCase)
            || name.Equals("docker.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("podman", StringComparison.OrdinalIgnoreCase)
            || name.Equals("podman.exe", StringComparison.OrdinalIgnoreCase);
    }
}
