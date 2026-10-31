using Lunate.Ai;

namespace Lunate.Agent.Tests;

public sealed class ContractsAssemblyTests
{
    [Fact]
    public void Tool_context_signature_types_are_defined_in_contracts()
    {
        Type[] contractTypes =
        [
            typeof(ITool),
            typeof(ToolContext),
            typeof(ToolResult),
            typeof(ToolStub),
            typeof(ToolStubParameter),
            typeof(ToolAnnotations),
            typeof(ToolExposure),
            typeof(ToolRisk),
            typeof(ToolConcurrency),
            typeof(ToolNamespace),
            typeof(ImageAttachment),
            typeof(IAgentEvents),
            typeof(GitIdentity),
            typeof(IFileChangeSink),
            typeof(IFileMutationQueue),
            typeof(ISubagentRunner),
            typeof(IModelAccess),
            typeof(ModelInfo),
            typeof(ModelPricing),
            typeof(ModelCapabilityDescriptor),
        ];

        Assert.All(
            contractTypes,
            type => Assert.Equal("Lunate.Contracts", type.Assembly.GetName().Name)
        );

        Type[] contextSignatureTypes = typeof(ToolContext)
            .GetMembers(
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly
            )
            .SelectMany(MemberSignatureTypes)
            .SelectMany(ExpandType)
            .Where(type => !IsAllowedContractSignatureType(type))
            .Distinct()
            .ToArray();

        Assert.Empty(contextSignatureTypes);
    }

    [Fact]
    public void Tool_context_exposes_output_limits_with_contract_defaults()
    {
        var context = new ToolContext("/work", new AgentEventChannel());

        Assert.Equal(30_000, context.ToolOutputLimit);
        Assert.Equal(2_000, context.SpillThresholdTokens);

        ToolContext configured = context with { ToolOutputLimit = 123, SpillThresholdTokens = 456 };

        Assert.Equal(123, configured.ToolOutputLimit);
        Assert.Equal(456, configured.SpillThresholdTokens);
    }

    [Fact]
    public void Tool_context_keeps_agent_execution_state_out_of_its_public_contract()
    {
        string[] publicPropertyNames = typeof(ToolContext)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Session", publicPropertyNames);
        Assert.DoesNotContain("Reads", publicPropertyNames);
        Assert.DoesNotContain("AgentState", publicPropertyNames);
        Assert.False(typeof(AgentToolContextState).IsPublic);
    }

    [Fact]
    public void Contracts_public_signature_closure_has_no_agent_or_other_lunate_dependencies()
    {
        Type[] unsupportedTypes = typeof(ToolContext)
            .Assembly.GetExportedTypes()
            .SelectMany(type =>
                type.GetMembers(
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.DeclaredOnly
                )
            )
            .SelectMany(MemberSignatureTypes)
            .SelectMany(ExpandType)
            .Where(type => !IsAllowedContractSignatureType(type))
            .Distinct()
            .ToArray();

        Assert.Empty(unsupportedTypes);
    }

    [Fact]
    public void The_former_agent_assembly_forwards_every_contract_type_in_its_namespace()
    {
        Type[] contractTypes = TypesInNamespace(typeof(ToolContext).Assembly, "Lunate.Agent");
        Type[] forwardedTypes = TypesInNamespace(
            typeof(AgentHarness).Assembly.GetForwardedTypes(),
            "Lunate.Agent"
        );

        Assert.Equal(contractTypes, forwardedTypes);
    }

    [Fact]
    public void The_former_ai_assembly_forwards_every_contract_type_in_its_namespace()
    {
        Type[] contractTypes = TypesInNamespace(typeof(ModelInfo).Assembly, "Lunate.Ai");
        Type[] forwardedTypes = TypesInNamespace(
            System.Reflection.Assembly.Load("Lunate.Ai").GetForwardedTypes(),
            "Lunate.Ai"
        );

        Assert.Equal(contractTypes, forwardedTypes);
    }

    [Fact]
    public void Old_assembly_qualified_contract_names_resolve_to_the_contract_copy()
    {
        Assert.Same(typeof(ToolContext), Type.GetType("Lunate.Agent.ToolContext, Lunate.Agent"));
        Assert.Same(typeof(ModelInfo), Type.GetType("Lunate.Ai.ModelInfo, Lunate.Ai"));
        Assert.Same(
            typeof(Lunate.Extensibility.Abstractions.ToolResultView),
            Type.GetType(
                "Lunate.Extensibility.Abstractions.ToolResultView, Lunate.Extensibility.Abstractions"
            )
        );
    }

    private static Type[] TypesInNamespace(
        System.Reflection.Assembly assembly,
        string @namespace
    ) => TypesInNamespace(assembly.GetExportedTypes(), @namespace);

    private static Type[] TypesInNamespace(IEnumerable<Type> types, string @namespace) =>
        types
            .Where(type => type.Namespace == @namespace)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Type> MemberSignatureTypes(System.Reflection.MemberInfo member) =>
        member switch
        {
            System.Reflection.PropertyInfo property => [property.PropertyType],
            System.Reflection.MethodInfo method =>
            [
                method.ReturnType,
                .. method.GetParameters().Select(parameter => parameter.ParameterType),
            ],
            System.Reflection.ConstructorInfo constructor => constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
            _ => [],
        };

    private static IEnumerable<Type> ExpandType(Type type)
    {
        if (type.HasElementType)
        {
            return [type, .. ExpandType(type.GetElementType()!)];
        }

        if (type.IsGenericType)
        {
            return [type, .. type.GetGenericArguments().SelectMany(ExpandType)];
        }

        return [type];
    }

    private static bool IsAllowedContractSignatureType(Type type)
    {
        string? assemblyName = type.Assembly.GetName().Name;
        return type.Assembly == typeof(ToolContext).Assembly
            || type.Assembly == typeof(object).Assembly
            || assemblyName?.StartsWith("System.", StringComparison.Ordinal) == true
            || assemblyName == "Microsoft.Extensions.AI.Abstractions";
    }
}
