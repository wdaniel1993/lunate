using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Testing;

/// <summary>Counts entries of a written session by kind, read back through the real <see cref="Session.Load"/>.</summary>
public static class SessionAssertions
{
    public static int CountMessages(string sessionPath, ChatRole? role = null) =>
        Session
            .Load(sessionPath)
            .Entries.OfType<SessionMessageEntry>()
            .Count(entry => role is null || entry.Message.Role == role);

    /// <summary>Counts every extension entry, regardless of the extension that wrote it.</summary>
    public static int CountExtensions(string sessionPath) => ExtensionEntries(sessionPath).Count();

    /// <summary>Counts the extension entries of one extension id (<c>ext/&lt;id&gt;/…</c>).</summary>
    public static int CountExtensions(string sessionPath, string extensionId) =>
        ExtensionEntries(sessionPath)
            .Count(entry =>
                entry.ExtensionType.StartsWith($"ext/{extensionId}/", StringComparison.Ordinal)
            );

    public static int CountNestedCalls(string sessionPath) =>
        Session.Load(sessionPath).Entries.OfType<SessionNestedCallsEntry>().Count();

    private static IEnumerable<SessionExtensionEntry> ExtensionEntries(string sessionPath) =>
        Session.Load(sessionPath).Entries.OfType<SessionExtensionEntry>();
}
