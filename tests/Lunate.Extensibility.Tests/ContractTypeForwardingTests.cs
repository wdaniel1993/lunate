using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ContractTypeForwardingTests
{
    [Fact]
    public void The_former_abstractions_assembly_forwards_contract_types_in_its_namespace()
    {
        Type[] contractTypes = typeof(ToolResultView)
            .Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(ToolResultView).Namespace)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Type[] forwardedTypes = System
            .Reflection.Assembly.Load("Lunate.Extensibility.Abstractions")
            .GetForwardedTypes()
            .Where(type => type.Namespace == typeof(ToolResultView).Namespace)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(contractTypes, forwardedTypes);
        Assert.Same(
            typeof(ToolResultView),
            Type.GetType(
                "Lunate.Extensibility.Abstractions.ToolResultView, Lunate.Extensibility.Abstractions"
            )
        );
    }
}
