using System.Text.Json;

namespace Lunate.Tui;

/// <summary>
/// The keymap, read from <c>keys.json</c> — the one place a key's meaning is written down, embedded
/// in this assembly at build time. Ordered: the first row that matches the key and applies to the
/// context wins, so specific rows precede general ones. The router, the in-app help and
/// <c>docs/guide.md</c>'s table all read this file: the table is generated from it by
/// <c>scripts/docs-sync</c>, so no prose can describe a binding the router does not have. A
/// <see cref="Remap"/> changes an existing binding's keys in place; it never adds a second row.
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

    private static readonly Lock Gate = new();

    public static IReadOnlyList<KeyBinding> All { get; private set; } = Load();

    /// <summary>Every row declared for one binding id (one row per declared key).</summary>
    internal static IReadOnlyList<KeyBinding> BindingsFor(string id) =>
        [.. All.Where(binding => string.Equals(binding.Id, id, StringComparison.Ordinal))];

    /// <summary>Each binding id with its declared keys, in catalogue order — the remap/test snapshot.</summary>
    internal static IReadOnlyList<(string Id, KeyPattern[] Keys)> Snapshot()
    {
        lock (Gate)
        {
            var snapshot = new List<(string, KeyPattern[])>();
            foreach (var group in All.GroupBy(binding => binding.Id))
            {
                snapshot.Add((group.Key, [.. group.Select(binding => binding.Key)]));
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Replaces one binding's declared keys in place: <paramref name="id"/>'s rows keep their
    /// context, description and order, and the catalogue gains no second declaration —
    /// <c>keys.json</c> stays the only place a key is declared.
    /// </summary>
    internal static string? Remap(string id, IReadOnlyList<KeyPattern> patterns)
    {
        lock (Gate)
        {
            List<KeyBinding> rows = [.. All];
            int index = rows.FindIndex(binding =>
                string.Equals(binding.Id, id, StringComparison.Ordinal)
            );
            if (index < 0)
            {
                return UnknownId(id);
            }

            if (patterns.Count == 0)
            {
                return "a binding keeps at least one key; a remap replaces its keys, it never disables it";
            }

            KeyBinding anchor = rows[index];
            string? conflict = Shadowed(rows, index, anchor, patterns);
            if (conflict is not null)
            {
                return conflict;
            }

            rows.RemoveAt(index);
            rows.InsertRange(
                index,
                patterns.Select(pattern => new KeyBinding(
                    id,
                    pattern,
                    anchor.Intent,
                    anchor.Contexts,
                    anchor.Description
                ))
            );
            All = rows;
            return null;
        }
    }

    /// <summary>
    /// The binding by id closest to <paramref name="id"/> in edit distance, the missing-id error's
    /// "did you mean" suggestion.
    /// </summary>
    internal static IReadOnlyList<string> NearestIds(string id, int count = 3)
    {
        var distances = new List<(string Id, int Distance)>();
        foreach (
            string catalogueId in All.Select(binding => binding.Id).Distinct(StringComparer.Ordinal)
        )
        {
            distances.Add((catalogueId, Levenshtein(id, catalogueId)));
        }

        return
        [
            .. distances
                .OrderBy(entry => entry.Distance)
                .Select(entry => entry.Id)
                .Distinct(StringComparer.Ordinal)
                .Take(count),
        ];
    }

    /// <summary>
    /// Whether an earlier row already answers one of the new keys in a context the binding applies
    /// to: such a remap would be dead (the earlier row wins), so it is refused with the owning id
    /// named. Modifier wildcards make the check conservative; a later row does not shadow.
    /// </summary>
    private static string? Shadowed(
        IReadOnlyList<KeyBinding> rows,
        int index,
        KeyBinding anchor,
        IReadOnlyList<KeyPattern> patterns
    )
    {
        int first = index;
        for (int candidate = 0; candidate < index; candidate++)
        {
            KeyBinding owner = rows[candidate];
            if (string.Equals(owner.Id, anchor.Id, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (KeyPattern pattern in patterns)
            {
                if (
                    Overlaps(owner.Key, pattern) && ContextsOverlap(owner.Contexts, anchor.Contexts)
                )
                {
                    return $"'{anchor.Id}' cannot take {KeyPatternFormatter.Describe(pattern)}: "
                        + $"'{owner.Id}' ({owner.Description}) already answers it in a context it matches";
                }
            }
        }

        return null;
    }

    private static bool ContextsOverlap(KeyContext owner, KeyContext target) =>
        owner == KeyContext.None || target == KeyContext.None || (owner & target) != 0;

    /// <summary>Whether two patterns can answer the same key (a null modifier matches either side).</summary>
    private static bool Overlaps(KeyPattern owner, KeyPattern candidate) =>
        owner.Kind == candidate.Kind
        && !Contradicts(owner.Ctrl, candidate.Ctrl)
        && !Contradicts(owner.Alt, candidate.Alt)
        && !Contradicts(owner.Shift, candidate.Shift)
        && (
            owner.Text is null
            || candidate.Text is null
            || string.Equals(owner.Text, candidate.Text, StringComparison.Ordinal)
        );

    private static bool Contradicts(bool? left, bool? right) =>
        left is { } a && right is { } b && a != b;

    private static string UnknownId(string id) =>
        $"{id} is not a binding id; the closest names are "
        + string.Join(", ", NearestIds(id).Select(name => $"'{name}'"));

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                int cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost
                );
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

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
                rows.Add(new KeyBinding(id, ParsePattern(id, key), intent, contexts, description));
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
