using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lunate.Roslyn;

/// <summary>The bounded declaration list produced by <see cref="OutlineCollector"/>.</summary>
internal readonly record struct BoundedOutline(
    IReadOnlyList<OutlineItem> Items,
    int Total,
    bool Truncated
);

/// <summary>
/// Walks a parsed file into body-free declaration items in source order: namespaces, types (nested
/// included) and members. Syntax-level only — no compilation or solution is needed — and parser
/// errors are tolerated: whatever the recovered syntax tree still contains is outlined.
/// </summary>
internal static class OutlineCollector
{
    internal const int MaxItems = 200;

    public static BoundedOutline Walk(SyntaxTree tree)
    {
        var items = new List<OutlineItem>();
        Collect(tree.GetRoot(), null, items);

        var capped = new CappedList<OutlineItem>(MaxItems);
        foreach (var item in items)
        {
            capped.Add(item);
        }

        return new BoundedOutline(capped.Items, capped.Total, capped.Truncated);
    }

    private static void Collect(SyntaxNode parent, string? container, List<OutlineItem> items)
    {
        foreach (var node in parent.ChildNodes())
        {
            switch (node)
            {
                case BaseNamespaceDeclarationSyntax declaration:
                    items.Add(Namespace(declaration, container));
                    Collect(declaration, Join(container, declaration.Name.ToString()), items);
                    break;
                case BaseTypeDeclarationSyntax type:
                    items.Add(
                        new OutlineItem(
                            TypeKindOf(type),
                            container,
                            type.Identifier.Text,
                            Signature(type),
                            Line(type)
                        )
                    );
                    if (type is not EnumDeclarationSyntax)
                    {
                        Collect(type, Join(container, type.Identifier.Text), items);
                    }

                    break;
                case DelegateDeclarationSyntax @delegate:
                    items.Add(
                        new OutlineItem(
                            "delegate",
                            container,
                            @delegate.Identifier.Text,
                            Signature(@delegate),
                            Line(@delegate)
                        )
                    );
                    break;
                case MemberDeclarationSyntax member:
                    items.Add(Member(member, container));
                    break;
            }
        }
    }

    private static OutlineItem Namespace(
        BaseNamespaceDeclarationSyntax declaration,
        string? container
    )
    {
        var full = declaration.Name.ToString();
        var lastDot = full.LastIndexOf('.');
        var name = lastDot < 0 ? full : full[(lastDot + 1)..];
        var parent = lastDot < 0 ? container : Join(container, full[..lastDot]);
        return new OutlineItem(
            "namespace",
            parent,
            name,
            Signature(declaration),
            Line(declaration)
        );
    }

    private static OutlineItem Member(MemberDeclarationSyntax member, string? container) =>
        member switch
        {
            MethodDeclarationSyntax method => Item(
                "method",
                container,
                method.Identifier.Text,
                member
            ),
            ConstructorDeclarationSyntax constructor => Item(
                "constructor",
                container,
                constructor.Identifier.Text,
                member
            ),
            DestructorDeclarationSyntax destructor => Item(
                "method",
                container,
                destructor.Identifier.Text,
                member
            ),
            PropertyDeclarationSyntax property => Item(
                "property",
                container,
                property.Identifier.Text,
                member
            ),
            IndexerDeclarationSyntax => Item("property", container, "this[]", member),
            FieldDeclarationSyntax field => Item(
                "field",
                container,
                Names(field.Declaration.Variables.Select(variable => variable.Identifier.Text)),
                member
            ),
            EventFieldDeclarationSyntax eventField => Item(
                "event",
                container,
                Names(
                    eventField.Declaration.Variables.Select(variable => variable.Identifier.Text)
                ),
                member
            ),
            EventDeclarationSyntax @event => Item(
                "event",
                container,
                @event.Identifier.Text,
                member
            ),
            OperatorDeclarationSyntax @operator => Item(
                "method",
                container,
                @operator.OperatorToken.Text,
                member
            ),
            ConversionOperatorDeclarationSyntax conversion => Item(
                "method",
                container,
                conversion.ImplicitOrExplicitKeyword.Text,
                member
            ),
            _ => Item(member.Kind().ToString().ToLowerInvariant(), container, string.Empty, member),
        };

    private static OutlineItem Item(string kind, string? container, string name, SyntaxNode node) =>
        new(kind, container, name, Signature(node), Line(node));

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);

    private static string Join(string? container, string name) =>
        container is null or "" ? name : string.Concat(container, ".", name);

    private static int Line(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string TypeKindOf(BaseTypeDeclarationSyntax type) =>
        type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            RecordDeclarationSyntax record
                when record.Kind() == SyntaxKind.RecordStructDeclaration => "struct",
            RecordDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            EnumDeclarationSyntax => "enum",
            _ => type.Kind().ToString().ToLowerInvariant(),
        };

    /// <summary>
    /// The declaration header: the node's text up to its body (initializer, expression body or
    /// brace block), with whitespace runs collapsed and a trailing semicolon removed.
    /// </summary>
    private static string Signature(SyntaxNode node)
    {
        var text = node.ToString();
        if (CutPosition(node) is { } cut)
        {
            var length = cut - node.SpanStart;
            if (length >= 0 && length <= text.Length)
            {
                text = text[..length];
            }
        }

        return Normalize(TrimSemicolon(text));
    }

    private static int? CutPosition(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax type => type.OpenBraceToken.SpanStart,
            NamespaceDeclarationSyntax block => block.OpenBraceToken.SpanStart,
            FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.SemicolonToken.SpanStart,
            MethodDeclarationSyntax method => BodyCut(
                method.Body?.OpenBraceToken.SpanStart,
                method.ExpressionBody?.ArrowToken.SpanStart
            ),
            ConstructorDeclarationSyntax constructor => BodyCut(
                constructor.Body?.OpenBraceToken.SpanStart,
                constructor.ExpressionBody?.ArrowToken.SpanStart
            ),
            DestructorDeclarationSyntax destructor => BodyCut(
                destructor.Body?.OpenBraceToken.SpanStart,
                destructor.ExpressionBody?.ArrowToken.SpanStart
            ),
            OperatorDeclarationSyntax @operator => BodyCut(
                @operator.Body?.OpenBraceToken.SpanStart,
                @operator.ExpressionBody?.ArrowToken.SpanStart
            ),
            ConversionOperatorDeclarationSyntax conversion => BodyCut(
                conversion.Body?.OpenBraceToken.SpanStart,
                conversion.ExpressionBody?.ArrowToken.SpanStart
            ),
            PropertyDeclarationSyntax property => BodyCut(
                property.Initializer?.SpanStart,
                property.ExpressionBody?.ArrowToken.SpanStart
            ),
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.ArrowToken.SpanStart,
            FieldDeclarationSyntax field => field.Declaration.Variables is [var variable, ..]
                ? variable.Initializer?.SpanStart
                : null,
            EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables
                is [var variable, ..]
                ? variable.Initializer?.SpanStart
                : null,
            _ => null,
        };

    private static int? BodyCut(int? first, int? second) =>
        (first, second) switch
        {
            (null, null) => null,
            ({ } one, null) => one,
            (null, { } two) => two,
            ({ } one, { } two) => Math.Min(one, two),
        };

    private static string TrimSemicolon(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith(';') ? trimmed[..^1].TrimEnd() : trimmed;
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
