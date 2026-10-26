using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// One row of <c>lunate packages</c>: what a record says, or the reason it says nothing. A record
/// that does not answer for itself keeps its directory's name as the id and reads <c>broken</c>,
/// because nothing in it can be believed — not even the version.
/// </summary>
internal sealed record InstalledPackageView
{
    internal required string Id { get; init; }

    internal string? Version { get; init; }

    internal PackageScope? Scope { get; init; }

    internal string? ExtensionId { get; init; }

    internal string? ExtensionVersion { get; init; }

    internal IReadOnlyList<string> Skills { get; init; } = [];

    internal required string RecordPath { get; init; }

    internal string? InstalledAt { get; init; }

    /// <summary>Why the record cannot be used; null when it reads.</summary>
    internal string? Error { get; init; }

    /// <summary>The content check, or null when <c>--verify</c> was not asked.</summary>
    internal PackageContentState? State { get; init; }

    internal bool Broken => Error is not null;
}

/// <summary>
/// The two shapes <c>lunate packages</c> prints (spec: "The <c>lunate packages</c> view"): one line
/// per package ordered by scope then id — id, version, scope, and what it provides — and, with
/// <c>--verify</c>, either <c>verified</c> or the changed and missing counts with one line naming
/// each affected file; or one JSON array carrying the line's fields plus <c>installedAt</c>,
/// <c>verified</c> and the counts. Every number is invariant and every newline is canonical.
/// </summary>
internal static class PackagesView
{
    internal static string Text(IReadOnlyList<InstalledPackageView> packages)
    {
        StringBuilder text = new();
        foreach (InstalledPackageView package in packages)
        {
            text.AppendLine(Line(package));
            if (package.Broken)
            {
                text.AppendLine($"  record: {package.RecordPath} — {package.Error}");
                continue;
            }

            if (package.State is { } state)
            {
                foreach (string path in state.Changed)
                {
                    text.AppendLine($"  changed: {path}");
                }

                foreach (string path in state.Missing)
                {
                    text.AppendLine($"  missing: {path}");
                }
            }
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    internal static string Json(IReadOnlyList<InstalledPackageView> packages)
    {
        var array = new JsonArray();
        foreach (InstalledPackageView package in packages)
        {
            array.Add(
                new JsonObject
                {
                    ["id"] = package.Id,
                    ["version"] = package.Version,
                    ["scope"] = package.Scope is { } scope ? ScopeText(scope) : null,
                    ["extension"] = package.ExtensionId is { } extension
                        ? new JsonObject
                        {
                            ["id"] = extension,
                            ["version"] = package.ExtensionVersion,
                        }
                        : null,
                    ["skills"] = Strings(package.Skills),
                    ["installedAt"] = package.InstalledAt,
                    ["record"] = package.RecordPath,
                    ["broken"] = package.Broken,
                    ["error"] = package.Error,
                    ["verified"] = package.State?.Clean,
                    ["changed"] = package.State?.Changed.Count ?? 0,
                    ["missing"] = package.State?.Missing.Count ?? 0,
                }
            );
        }

        return array.ToJsonString();
    }

    internal static string ScopeText(PackageScope scope) =>
        scope == PackageScope.Project ? "project" : "user";

    private static string Line(InstalledPackageView package)
    {
        if (package.Broken)
        {
            return $"packages: {package.Id} — broken";
        }

        List<string> parts = [ScopeText(package.Scope!.Value)];
        if (package.ExtensionId is { } extension)
        {
            parts.Add(
                package.ExtensionVersion is { Length: > 0 } version
                    ? $"extension {extension} {version}"
                    : $"extension {extension}"
            );
        }

        if (package.Skills.Count > 0)
        {
            parts.Add($"skills {string.Join(", ", package.Skills)}");
        }

        if (package.State is { } state)
        {
            parts.Add(
                state.Clean
                    ? "verified"
                    : FormattableString.Invariant(
                        $"{state.Changed.Count} changed, {state.Missing.Count} missing"
                    )
            );
        }

        return FormattableString.Invariant(
            $"packages: {package.Id} {package.Version} — {string.Join(", ", parts)}"
        );
    }

    private static JsonArray Strings(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
