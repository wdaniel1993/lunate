using System.Reflection;

namespace Lunate.Tui.Tests;

public sealed class PublicApiTests
{
    private static readonly string[] DocumentedSurface =
    [
        "Lunate.Tui.ConsoleSize",
        "Lunate.Tui.ConsoleSupport",
        "Lunate.Tui.IConsoleIO",
        "Lunate.Tui.InputLine",
        "Lunate.Tui.InputLineState",
        "Lunate.Tui.KeyEvent",
        "Lunate.Tui.KeyKind",
        "Lunate.Tui.MarkdownRenderer",
        "Lunate.Tui.TechnicalText",
    ];

    [Fact]
    public void The_public_surface_is_exactly_the_documented_set()
    {
        var actual = typeof(IConsoleIO)
            .Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DocumentedSurface.Order(StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void No_public_member_exposes_a_system_reactive_type()
    {
        var offenders = new List<string>();
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var type in typeof(IConsoleIO).Assembly.GetExportedTypes())
        {
            if (MentionsRx(type))
            {
                offenders.Add(type.FullName!);
            }

            foreach (var member in type.GetMembers(flags))
            {
                if (SignatureTypes(member).Any(MentionsRx))
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) =>
        member switch
        {
            FieldInfo field => [field.FieldType],
            PropertyInfo property => [property.PropertyType],
            EventInfo @event when @event.EventHandlerType is { } handler => [handler],
            MethodInfo method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            ConstructorInfo constructor => constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
            _ => [],
        };

    private static bool MentionsRx(Type type) =>
        type.Namespace?.StartsWith("System.Reactive", StringComparison.Ordinal) == true
        || (type.HasElementType && type.GetElementType() is { } element && MentionsRx(element))
        || (type.IsGenericType && type.GetGenericArguments().Any(MentionsRx));
}
