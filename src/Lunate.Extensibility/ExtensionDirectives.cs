namespace Lunate.Extensibility;

/// <summary>
/// One header directive: its name (without the <c>#:</c> prefix) and the
/// <see cref="Abstractions.ExtensionManifest"/> field it declares, or null for a directive that
/// declares no manifest field (<c>#:extension</c> sets the id and version, <c>#:package</c> is a
/// restore input and <c>#:generators</c> is a build-time opt-in).
/// </summary>
public sealed record ExtensionDirective(string Name, string? ManifestField);

/// <summary>
/// The header grammar's directive table. The scanner dispatches on <see cref="Name"/>, and the
/// table is the one place that says which manifest declaration field each directive mirrors — the
/// manifest-mirror test walks <see cref="Abstractions.ExtensionManifest"/>'s declaration lists and
/// fails when one has no row here, so a manifest field cannot be added without a directive.
/// </summary>
public static class ExtensionDirectives
{
    /// <summary>The one required directive: <c>#:extension &lt;id&gt;@&lt;exact version&gt;</c>.</summary>
    public const string Extension = "extension";

    /// <summary>Declares a tool name; mirrors the manifest's <c>tools</c>.</summary>
    public const string Tool = "tool";

    /// <summary>Declares a command name; mirrors the manifest's <c>commands</c>.</summary>
    public const string Command = "command";

    /// <summary>Declares a hook name; mirrors the manifest's <c>hooks</c>.</summary>
    public const string Hook = "hook";

    /// <summary>Declares a service name; mirrors the manifest's <c>services</c>.</summary>
    public const string Service = "service";

    /// <summary>Declares a free-form capability; mirrors the manifest's <c>capabilities</c>.</summary>
    public const string Capability = "capability";

    /// <summary>Declares a model-provider id; mirrors the manifest's <c>modelProviders</c>.</summary>
    public const string Provider = "provider";

    /// <summary>Declares a package to restore, in the documented .NET 10 syntax.</summary>
    public const string Package = "package";

    /// <summary>Opts in to the declared packages' analyzers and source generators.</summary>
    public const string Generators = "generators";

    private static readonly ExtensionDirective[] Table =
    [
        new(Extension, null),
        new(Tool, "Tools"),
        new(Command, "Commands"),
        new(Hook, "Hooks"),
        new(Service, "Services"),
        new(Capability, "Capabilities"),
        new(Provider, "ModelProviders"),
        new(Package, null),
        new(Generators, null),
    ];

    /// <summary>Every directive the scanner knows, in documentation order.</summary>
    public static IReadOnlyList<ExtensionDirective> All => Table;

    /// <summary>
    /// The directive that declares <paramref name="manifestField"/>, or null when the manifest
    /// field has no directive. Callers pass the manifest's own property names (<c>Tools</c>,
    /// <c>Commands</c>, <c>Hooks</c>, <c>Services</c>, <c>Capabilities</c>,
    /// <c>ModelProviders</c>), which is what the mirror test reflects over.
    /// </summary>
    public static ExtensionDirective? ForManifestField(string manifestField) =>
        Table.FirstOrDefault(directive =>
            directive.ManifestField is { } field
            && string.Equals(field, manifestField, StringComparison.Ordinal)
        );

    /// <summary>
    /// The values a header declares for one manifest declaration field, read back through the same
    /// table the scanner uses. Throws when the field has no directive — a manifest field without one
    /// is the gap the mirror test guards.
    /// </summary>
    public static IReadOnlyList<string> DeclaredValues(ExtensionHeader header, string manifestField)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestField);

        _ =
            ForManifestField(manifestField)
            ?? throw new ArgumentOutOfRangeException(
                nameof(manifestField),
                manifestField,
                "the manifest declaration field has no header directive."
            );

        return manifestField switch
        {
            "Tools" => header.Tools,
            "Commands" => header.Commands,
            "Hooks" => header.Hooks,
            "Services" => header.Services,
            "Capabilities" => header.Capabilities,
            "ModelProviders" => header.Providers,
            _ => throw new ArgumentOutOfRangeException(
                nameof(manifestField),
                manifestField,
                "the manifest declaration field has no header reader."
            ),
        };
    }
}
