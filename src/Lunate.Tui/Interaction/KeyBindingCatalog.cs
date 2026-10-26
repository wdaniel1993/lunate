using System.Text.Json;

namespace Lunate.Tui;

/// <summary>
/// The keymap, read from <c>keys.json</c> — the one place a key's meaning is written down, embedded
/// in this assembly at build time. Ordered: the first row that matches the key and applies to the
/// context wins, so specific rows precede general ones. The router, the in-app help and
/// <c>docs/guide.md</c>'s table all read this file: the table is generated from it by
/// <c>scripts/docs-sync</c>, so no prose can describe a binding the router does not have.
/// </summary>
/// <remarks>
/// A binding is <c>{ "id", "keys", "intent", "context", "description" }</c>: a stable id (the handle
/// a future user bindings file overrides), the key patterns it answers to, the intent the router
/// returns, the context flags that must all be present (<c>None</c> = every state), and the help
/// text. An absent modifier is unconstrained, which is how <c>Escape</c> matches any modifiers
/// held. The file also carries <c>guideRows</c> (the guide's table rows, each naming the bindings
/// it documents); this assembly ignores them — they exist so the docs and the router cannot drift.
/// </remarks>
internal static class KeyBindingCatalog
{
    /// <summary>The embedded resource that carries the keymap.</summary>
    internal const string ResourceName = "Lunate.Tui.keys.json";

    public static IReadOnlyList<KeyBinding> All { get; } = Load();

    private static IReadOnlyList<KeyBinding> Load()
    {
        using Stream stream =
            typeof(KeyBindingCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"the keymap resource '{ResourceName}' is missing from the {typeof(KeyBindingCatalog).Assembly.GetName().Name} assembly; "
                    + "keys.json must be embedded (see the EmbeddedResource item in Lunate.Tui.csproj)"
            );
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("bindings", out JsonElement bindings))
        {
            throw new InvalidOperationException(
                $"'{ResourceName}' has no 'bindings' array; the keymap is unreadable and every key would be dead"
            );
        }

        List<KeyBinding> rows = [];
        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            string id = RequiredString(binding, "id");
            RoutedKey intent = ParseIntent(id, binding);
            KeyContext contexts = ParseContexts(id, binding);
            string description = RequiredString(binding, "description");
            if (
                !binding.TryGetProperty("keys", out JsonElement keys)
                || keys.ValueKind != JsonValueKind.Array
            )
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}': binding '{id}' has no 'keys' array, so it can never match a key"
                );
            }

            var patterns = 0;
            foreach (JsonElement key in keys.EnumerateArray())
            {
                rows.Add(new KeyBinding(ParsePattern(id, key), intent, contexts, description));
                patterns++;
            }

            if (patterns == 0)
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}': binding '{id}' lists no key patterns, so it can never match a key"
                );
            }
        }

        return rows;
    }

    private static KeyPattern ParsePattern(string id, JsonElement key)
    {
        if (key.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"'{ResourceName}': a key of binding '{id}' is not an object"
            );
        }

        KeyKind kind = ParseEnum<KeyKind>(id, RequiredString(key, "kind"), "kind");
        return new KeyPattern(
            kind,
            Modifier(key, "ctrl"),
            Modifier(key, "alt"),
            Modifier(key, "shift"),
            key.TryGetProperty("text", out JsonElement text) ? text.GetString() : null
        );
    }

    /// <summary>A modifier that is absent is unconstrained; one that is present is required.</summary>
    private static bool? Modifier(JsonElement key, string name) =>
        key.TryGetProperty(name, out JsonElement value) ? value.GetBoolean() : null;

    private static RoutedKey ParseIntent(string id, JsonElement binding) =>
        ParseEnum<RoutedKey>(id, RequiredString(binding, "intent"), "intent");

    /// <summary>The flags that must all be present; a name list is ORed, as the enum is flags.</summary>
    private static KeyContext ParseContexts(string id, JsonElement binding)
    {
        if (!binding.TryGetProperty("context", out JsonElement context))
        {
            return KeyContext.None;
        }

        return context.ValueKind switch
        {
            JsonValueKind.String => ParseEnum<KeyContext>(id, context.GetString() ?? "", "context"),
            JsonValueKind.Array => ArrayContexts(id, context),
            _ => throw new InvalidOperationException(
                $"'{ResourceName}': binding '{id}' has a 'context' that is neither a name nor a list of names"
            ),
        };
    }

    private static KeyContext ArrayContexts(string id, JsonElement context)
    {
        var contexts = KeyContext.None;
        foreach (JsonElement name in context.EnumerateArray())
        {
            contexts |= ParseEnum<KeyContext>(id, name.GetString() ?? "", "context");
        }

        return contexts;
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException(
                $"'{ResourceName}': an entry is missing its '{name}' string, so the keymap cannot be read"
            );

    /// <summary>
    /// Parses a name (never a number: <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> accepts
    /// "512" as <c>Idle</c>, which would let a typo in a flag read as a defined state).
    /// </summary>
    private static TEnum ParseEnum<TEnum>(string id, string name, string field)
        where TEnum : struct, Enum
    {
        bool named = name.Length > 0 && !char.IsDigit(name[0]);
        if (
            !named
            || !Enum.TryParse(name, ignoreCase: false, out TEnum value)
            || !Enum.IsDefined(value)
        )
        {
            throw new InvalidOperationException(
                $"'{ResourceName}': binding '{id}' has {field} '{name}', which is not a {typeof(TEnum).Name} member"
            );
        }

        return value;
    }
}
