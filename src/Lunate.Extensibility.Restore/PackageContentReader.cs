using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The content a package manifest declares: the extension folder is judged by the existing manifest
/// parser and the existing API gate, each skill folder by the existing frontmatter parser, and the
/// folders no kind names are collected so the output can name them once (design D2).
/// </summary>
internal static class PackageContentReader
{
    internal static PackageExtension ReadExtension(
        PackageArchive archive,
        string folder,
        string label
    )
    {
        if (archive.FilesUnder(folder).Count == 0)
        {
            throw new InvalidDataException(
                $"{label}: lunate.package.json names the 'extension' folder '{folder}', but the "
                    + "package has no files under it. Add the extension's files or remove "
                    + "'extension' from the manifest."
            );
        }

        string manifestPath = $"{folder}/extension.json";
        string? json =
            archive.ReadText(manifestPath)
            ?? throw new InvalidDataException(
                $"{label}: the 'extension' folder '{folder}' has no extension.json; an extension "
                    + "folder carries its manifest at its root. Add it or remove 'extension' from "
                    + "lunate.package.json."
            );

        ExtensionManifest manifest;
        try
        {
            manifest = ExtensionManifest.Parse(json, manifestPath);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"{label}: the extension manifest could not be used: {exception.Message}",
                exception
            );
        }

        if (ExtensionApiGate.RefuseRange(manifest.Id, manifest.ApiVersion) is { } refusal)
        {
            throw new InvalidDataException($"{label}: {refusal}");
        }

        if (
            manifest.EntryAssembly is { } entryAssembly
            && !archive.Contains($"{folder}/{entryAssembly}")
        )
        {
            throw new InvalidDataException(
                $"{label}: extension '{manifest.Id}' declares the entry assembly "
                    + $"'{entryAssembly}', which is not in '{folder}/'. Publish the assembly the "
                    + "manifest names, or fix 'entryAssembly'."
            );
        }

        return new PackageExtension(folder, manifest);
    }

    internal static IReadOnlyList<PackageSkill> ReadSkills(
        PackageArchive archive,
        string folder,
        string label
    )
    {
        string prefix = folder + "/";
        SortedSet<string> directories = new(StringComparer.Ordinal);
        foreach (string file in archive.FilesUnder(folder))
        {
            string rest = file[prefix.Length..];
            int separator = rest.IndexOf('/');
            if (separator > 0)
            {
                directories.Add(rest[..separator]);
            }
        }

        if (directories.Count == 0)
        {
            throw new InvalidDataException(
                $"{label}: lunate.package.json names the 'skills' folder '{folder}', but it holds no "
                    + "skill directories. Add '<name>/SKILL.md' under it or remove 'skills' from the "
                    + "manifest."
            );
        }

        List<PackageSkill> skills = [];
        Dictionary<string, string> named = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            string skillDirectory = $"{prefix}{directory}";
            string? text = archive.ReadText($"{skillDirectory}/SKILL.md");
            if (text is null)
            {
                throw new InvalidDataException(
                    $"{label}: skill directory '{skillDirectory}' has no SKILL.md; every skill "
                        + "folder carries one at its root. Add it or remove the folder."
                );
            }

            SkillFrontmatter frontmatter = SkillFrontmatterParser.Parse(text);
            if (frontmatter.Error is { } reason)
            {
                throw new InvalidDataException(
                    $"{label}: skill '{directory}' is not usable: {reason}. Fix its SKILL.md "
                        + "frontmatter and republish."
                );
            }

            if (!string.Equals(frontmatter.Name, directory, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{label}: skill '{directory}' declares the name '{frontmatter.Name}'; a "
                        + "skill's name must equal its directory name. Rename one of them."
                );
            }

            if (named.TryGetValue(frontmatter.Name!, out string? existing))
            {
                throw new InvalidDataException(
                    $"{label}: skills '{prefix}{existing}' and '{skillDirectory}' declare the same "
                        + $"name '{frontmatter.Name}' more than once; a package installs each skill "
                        + "name once. Rename one directory."
                );
            }

            named[frontmatter.Name!] = directory;
            skills.Add(
                new PackageSkill(skillDirectory, frontmatter.Name!, frontmatter.Description!)
            );
        }

        return skills;
    }

    /// <summary>
    /// The top-level folders the package carries that no manifest kind names, sorted: NuGet's own
    /// metadata is not content, and neither is a root file. They are never installed; the report
    /// names them once so nothing disappears silently.
    /// </summary>
    internal static IReadOnlyList<string> IgnoredFolders(
        PackageArchive archive,
        PackageManifest manifest
    )
    {
        List<string> declared = [.. manifest.Content.Select(item => item.Directory + "/")];
        SortedSet<string> ignored = new(StringComparer.Ordinal);
        foreach (string file in archive.Files)
        {
            if (PackagePaths.IsNuGetMetadata(file))
            {
                continue;
            }

            if (declared.Any(prefix => file.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            int separator = file.IndexOf('/');
            if (separator > 0)
            {
                ignored.Add(file[..separator]);
            }
        }

        return [.. ignored];
    }
}
