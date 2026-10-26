using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// What <c>lunate install</c> prints (spec: "Install output and <c>--dry-run</c>"): the identity —
/// id, exact version, source, SHA-512, what the package carries and the scope it lands in — then
/// one line saying what was written and where; an already-installed version answers from its
/// record; a dry run is the same identity with nothing written. The JSON object carries the same
/// fields, canonical newlines and no colour.
/// </summary>
internal static class InstallView
{
    internal static string Text(
        PackageInstallResult result,
        string scope,
        string scopeRoot,
        string? worktree
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> lines = [.. Identity(result, scope, worktree)];
        lines.Add(
            result.Status switch
            {
                PackageInstallStatus.AlreadyInstalled =>
                    $"already installed: {result.Record.Package.Version} is unchanged (record '{result.RecordPath}')",
                PackageInstallStatus.Upgraded => Invariant(
                    $"upgraded: {result.PreviousVersion} to {result.Record.Package.Version}, {result.WrittenPaths.Count} files under '{scopeRoot}' (record '{result.RecordPath}')"
                ),
                PackageInstallStatus.Repaired when result.WrittenPaths.Count == 0 => Invariant(
                    $"unchanged: every recorded file under '{scopeRoot}' matches the record (record '{result.RecordPath}')"
                ),
                PackageInstallStatus.Repaired => Invariant(
                    $"repaired: {result.WrittenPaths.Count} of {result.Record.Content.Paths.Count} files under '{scopeRoot}' (record '{result.RecordPath}')"
                ),
                _ => Invariant(
                    $"installed: {result.WrittenPaths.Count} files under '{scopeRoot}' (record '{result.RecordPath}')"
                ),
            }
        );
        return Lines(lines);
    }

    internal static string DryRun(PackageDescription description, string scope, string? worktree) =>
        Lines([
            .. PackageReport.Identity(
                description.Package,
                string.Join(", ", description.Sources),
                scope,
                worktree,
                description.Dependencies
            ),
            "dry-run: nothing was written",
        ]);

    internal static string Json(PackageInstallResult result, string scope, string? worktree)
    {
        ArgumentNullException.ThrowIfNull(result);

        var document = Document(
            result.Record.Package.Id,
            result.Record.Package.Version,
            string.Join(", ", result.Record.Package.Sources),
            result.Record.Package.Sha512,
            result.Record.Content.ExtensionId,
            result.Record.Content.ExtensionVersion,
            result.Record.Content.Skills,
            result.Dependencies,
            scope,
            worktree
        );
        document["record"] = result.RecordPath;
        document["written"] = Strings(result.WrittenPaths);
        document["alreadyInstalled"] = result.Status == PackageInstallStatus.AlreadyInstalled;
        document["repaired"] = result.Status == PackageInstallStatus.Repaired;
        document["previousVersion"] = result.PreviousVersion;
        document["removed"] = Strings(result.RemovedPaths);
        document["dryRun"] = false;
        return document.ToJsonString();
    }

    internal static string Json(PackageDescription description, string scope, string? worktree)
    {
        ArgumentNullException.ThrowIfNull(description);

        PackageInfo package = description.Package;
        var document = Document(
            package.Id,
            package.Version,
            string.Join(", ", description.Sources),
            package.Sha512,
            package.Extension?.Manifest.Id,
            package.Extension?.Manifest.Version,
            [.. package.Skills.Select(skill => skill.Name)],
            description.Dependencies,
            scope,
            worktree
        );
        document["record"] = null;
        document["written"] = new JsonArray();
        document["alreadyInstalled"] = false;
        document["dryRun"] = true;
        return document.ToJsonString();
    }

    private static JsonObject Document(
        string id,
        string version,
        string source,
        string sha512,
        string? extensionId,
        string? extensionVersion,
        IReadOnlyList<string> skills,
        IReadOnlyList<ResolvedDependency> dependencies,
        string scope,
        string? worktree
    )
    {
        var closure = new JsonArray();
        foreach (ResolvedDependency dependency in dependencies)
        {
            closure.Add(
                new JsonObject { ["id"] = dependency.Id, ["version"] = dependency.Version }
            );
        }

        return new()
        {
            ["id"] = id,
            ["version"] = version,
            ["source"] = source,
            ["sha512"] = sha512,
            ["extension"] = extensionId is null
                ? null
                : new JsonObject { ["id"] = extensionId, ["version"] = extensionVersion },
            ["skills"] = Strings(skills),
            ["dependencies"] = closure,
            ["scope"] = scope,
            ["worktree"] = worktree,
        };
    }

    private static List<string> Identity(
        PackageInstallResult result,
        string scope,
        string? worktree
    ) =>
        result.Resolved is { } package
            ?
            [
                .. PackageReport.Identity(
                    package,
                    string.Join(", ", result.Record.Package.Sources),
                    scope,
                    worktree,
                    result.Dependencies
                ),
            ]
            :
            [
                Invariant($"package: {result.Record.Package.Id} {result.Record.Package.Version}"),
                Invariant($"source: {string.Join(", ", result.Record.Package.Sources)}"),
                Invariant($"sha512: {result.Record.Package.Sha512}"),
                result.Dependencies.Count == 0
                    ? "dependencies: none"
                    : Invariant(
                        $"dependencies: {string.Join(", ", result.Dependencies.Select(dependency => $"{dependency.Id} {dependency.Version}"))}"
                    ),
                Invariant($"scope: {scope}"),
            ];

    private static string Lines(IReadOnlyList<string> lines) =>
        string.Join('\n', lines).ReplaceLineEndings("\n") + "\n";

    private static JsonArray Strings(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
