namespace Lunate.Agent.Tests;

public sealed class ToolAnnotationNamesTests
{
    [Fact]
    public void No_annotations_map_to_an_empty_list()
    {
        Assert.Empty(ToolAnnotationNames.WireNames(null));
        Assert.Empty(ToolAnnotationNames.WireNames(new ToolAnnotations()));
    }

    [Fact]
    public void Declared_annotations_map_to_lowercase_kebab_names_in_declaration_order()
    {
        var annotations = new ToolAnnotations(
            ReadOnly: true,
            Destructive: true,
            Idempotent: true,
            OpenWorld: true
        );

        Assert.Equal(
            ["read-only", "destructive", "idempotent", "open-world"],
            ToolAnnotationNames.WireNames(annotations)
        );
    }

    [Fact]
    public void Only_declared_annotations_appear_in_declaration_order()
    {
        var annotations = new ToolAnnotations(Destructive: true, OpenWorld: true);

        Assert.Equal(["destructive", "open-world"], ToolAnnotationNames.WireNames(annotations));
    }
}
