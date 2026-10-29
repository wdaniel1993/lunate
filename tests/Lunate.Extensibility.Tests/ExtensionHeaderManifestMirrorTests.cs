using System.Reflection;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The mirror rule (spec: "Every manifest declaration kind has a directive"): the header covers
/// every declaration list the packaged manifest has, so a single file can never be unable to
/// declare something a package can. The reflection walks the manifest type itself, so adding a
/// declaration field without a directive fails here instead of shipping as a silent gap.
/// </summary>
public sealed class ExtensionHeaderManifestMirrorTests
{
    [Fact]
    public void Every_manifest_declaration_list_has_a_directive_that_declares_it()
    {
        IReadOnlyList<PropertyInfo> declarations = DeclarationLists();

        // Pinned by name so a change to the manifest's shape (or to the reflection's criteria)
        // cannot silently shrink the set this test walks.
        Assert.Equal(
            ["Capabilities", "Commands", "Hooks", "ModelProviders", "Services", "Tools"],
            declarations.Select(property => property.Name)
        );
        foreach (PropertyInfo property in declarations)
        {
            if (ExtensionDirectives.ForManifestField(property.Name) is not { } directive)
            {
                Assert.Fail(
                    $"ExtensionManifest.{property.Name} is a declaration list with no header "
                        + "directive. Add the directive to ExtensionDirectives and to the scanner, "
                        + "so a single-file extension can declare it too."
                );
                return;
            }

            string source =
                $"#:extension mirror-test@1.0.0\n#:{directive.Name} mirror-value\n\npublic sealed class M;\n";
            ExtensionHeader header = ExtensionHeaderScanner.Scan(source);

            Assert.Empty(header.Errors);
            Assert.Equal(
                ["mirror-value"],
                ExtensionDirectives.DeclaredValues(header, property.Name)
            );
        }
    }

    [Fact]
    public void No_directive_names_a_manifest_field_that_does_not_exist()
    {
        IReadOnlyList<string> manifestFields =
        [
            .. DeclarationLists().Select(property => property.Name),
        ];

        IEnumerable<ExtensionDirective> mapping = ExtensionDirectives.All.Where(directive =>
            directive.ManifestField is not null
        );

        foreach (ExtensionDirective directive in mapping)
        {
            Assert.Contains(directive.ManifestField!, manifestFields);
        }
    }

    [Fact]
    public void Declared_values_reads_every_declaration_kind()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension mirror-test@1.0.0
            #:tool a-tool
            #:command a-command
            #:hook a-hook
            #:service a-service
            #:capability a-capability
            #:provider a-provider

            public sealed class M;
            """
        );

        foreach (PropertyInfo property in DeclarationLists())
        {
            Assert.NotEmpty(ExtensionDirectives.DeclaredValues(header, property.Name));
        }
    }

    /// <summary>
    /// The directive-backed declaration lists: the fields the header requirement enumerates
    /// (<c>tools</c>, <c>commands</c>, <c>hooks</c>, <c>services</c>, <c>capabilities</c>,
    /// <c>modelProviders</c>), each mirrored one to one by a <c>#:</c> directive.
    /// <c>WorkspaceServices</c> is structured config — a name plus an optional cap per entry — not
    /// a contribution a single-file header can declare as a name list; a single-file extension
    /// registers services through its factory and the loader cross-checks it like any other.
    /// Scalars (id, version, apiVersion, entryAssembly, runtime, settingsSchema) are not
    /// declarations of a contribution and need no directive.
    /// </summary>
    private static IReadOnlyList<PropertyInfo> DeclarationLists() =>
        [
            .. typeof(ExtensionManifest)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                    && property.Name != "WorkspaceServices"
                )
                .OrderBy(property => property.Name, StringComparer.Ordinal),
        ];
}
