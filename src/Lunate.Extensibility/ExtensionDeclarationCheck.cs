namespace Lunate.Extensibility;

/// <summary>Which declaration surface a name belongs to (the six the header mirrors).</summary>
public enum ExtensionDeclarationKind
{
    Tool,
    Command,
    Hook,
    Service,
    Capability,
    Provider,
}

/// <summary>One declared or registered contribution: its kind and its name.</summary>
public sealed record ExtensionDeclaration(ExtensionDeclarationKind Kind, string Name);

/// <summary>
/// The outcome of comparing a header with what the extension's code registered: the registrations
/// the header does not declare (each refuses the load) and the declarations nothing provided (each
/// is a warning, shown by <c>/extensions</c> as <c>declared, not provided</c>).
/// </summary>
public sealed record ExtensionDeclarationCheckResult(
    IReadOnlyList<string> Refusals,
    IReadOnlyList<ExtensionDeclaration> DeclaredNotProvided
);

/// <summary>
/// The declaration rule for a single-file extension: the header is the only place a declaration
/// can come from, so a registration the header does not declare refuses the load — the header is
/// what the user trusted before any code ran. The reverse is a warning only: a declared name the
/// code never registers was still approved, and nothing is offered for it.
/// </summary>
public static class ExtensionDeclarationCheck
{
    /// <summary>
    /// Compares <paramref name="header"/> with <paramref name="registered"/> and returns the
    /// refusals (registered but not declared) and the declarations nothing provided.
    /// </summary>
    public static ExtensionDeclarationCheckResult Check(
        ExtensionHeader header,
        IReadOnlyList<ExtensionDeclaration> registered
    )
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(registered);

        List<string> refusals = [];
        foreach (ExtensionDeclaration registration in registered)
        {
            if (
                !Declared(header, registration.Kind)
                    .Contains(registration.Name, StringComparer.Ordinal)
            )
            {
                refusals.Add(
                    $"{KindText(registration.Kind)} '{registration.Name}' is registered by code but "
                        + $"not declared in the header; add '{DirectiveFor(registration.Kind)} "
                        + $"{registration.Name}' to the file's leading block, or remove the "
                        + "registration."
                );
            }
        }

        List<ExtensionDeclaration> declaredNotProvided = [];
        foreach (ExtensionDeclarationKind kind in Enum.GetValues<ExtensionDeclarationKind>())
        {
            foreach (string name in Declared(header, kind))
            {
                bool provided = registered.Any(entry =>
                    entry.Kind == kind && string.Equals(entry.Name, name, StringComparison.Ordinal)
                );
                if (!provided)
                {
                    declaredNotProvided.Add(new ExtensionDeclaration(kind, name));
                }
            }
        }

        return new ExtensionDeclarationCheckResult(refusals, declaredNotProvided);
    }

    /// <summary>The kind's word as a message reads it (<c>tool</c>, <c>provider</c>, …).</summary>
    public static string KindText(ExtensionDeclarationKind kind) =>
        kind switch
        {
            ExtensionDeclarationKind.Tool => "tool",
            ExtensionDeclarationKind.Command => "command",
            ExtensionDeclarationKind.Hook => "hook",
            ExtensionDeclarationKind.Service => "service",
            ExtensionDeclarationKind.Capability => "capability",
            ExtensionDeclarationKind.Provider => "model provider",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>The <c>#:</c> directive that declares a kind, for the refusal message.</summary>
    public static string DirectiveFor(ExtensionDeclarationKind kind) =>
        kind switch
        {
            ExtensionDeclarationKind.Tool => "#:" + ExtensionDirectives.Tool,
            ExtensionDeclarationKind.Command => "#:" + ExtensionDirectives.Command,
            ExtensionDeclarationKind.Hook => "#:" + ExtensionDirectives.Hook,
            ExtensionDeclarationKind.Service => "#:" + ExtensionDirectives.Service,
            ExtensionDeclarationKind.Capability => "#:" + ExtensionDirectives.Capability,
            ExtensionDeclarationKind.Provider => "#:" + ExtensionDirectives.Provider,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>The names a header declares for one kind.</summary>
    public static IReadOnlyList<string> Declared(
        ExtensionHeader header,
        ExtensionDeclarationKind kind
    )
    {
        ArgumentNullException.ThrowIfNull(header);

        return kind switch
        {
            ExtensionDeclarationKind.Tool => header.Tools,
            ExtensionDeclarationKind.Command => header.Commands,
            ExtensionDeclarationKind.Hook => header.Hooks,
            ExtensionDeclarationKind.Service => header.Services,
            ExtensionDeclarationKind.Capability => header.Capabilities,
            ExtensionDeclarationKind.Provider => header.Providers,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}
