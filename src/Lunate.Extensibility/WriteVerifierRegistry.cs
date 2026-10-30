using System.IO.Enumeration;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>Host-owned write verifiers selected by their manifest-declared filename patterns.</summary>
public sealed class WriteVerifierRegistry
{
    private readonly Dictionary<string, List<Entry>> _extensions = new(StringComparer.Ordinal);

    internal void Register(
        string extensionId,
        WriteVerifierRegistration registration,
        IReadOnlyList<WriteVerifierDeclaration> declarations
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Name);
        ArgumentNullException.ThrowIfNull(registration.Verifier);

        if (!_extensions.TryGetValue(extensionId, out List<Entry>? entries))
        {
            entries = [];
            _extensions.Add(extensionId, entries);
        }

        if (entries.Any(entry => entry.Registration.Name == registration.Name))
        {
            throw new InvalidOperationException(
                $"extension '{extensionId}': write verifier '{registration.Name}' is registered more than once."
            );
        }

        string[] patterns =
            declarations
                .FirstOrDefault(declaration => declaration.Name == registration.Name)
                ?.Patterns.ToArray()
            ?? [];
        entries.Add(new Entry(registration, patterns));
    }

    /// <summary>Returns the first verifier whose declared basename glob matches the relative path.</summary>
    public IWriteVerifier? Find(string normalizedRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRelativePath);
        string basename = normalizedRelativePath.Replace('\\', '/').Split('/').Last();

        foreach (Entry entry in _extensions.Values.SelectMany(entries => entries))
        {
            if (
                entry.Patterns.Any(pattern =>
                    FileSystemName.MatchesSimpleExpression(pattern, basename, ignoreCase: true)
                )
            )
            {
                return entry.Registration.Verifier;
            }
        }

        return null;
    }

    internal IReadOnlyList<string> RegisteredNames(string extensionId) =>
        _extensions.TryGetValue(extensionId, out List<Entry>? entries)
            ? entries.Select(entry => entry.Registration.Name).ToArray()
            : [];

    internal void Unregister(string extensionId) => _extensions.Remove(extensionId);

    private sealed record Entry(WriteVerifierRegistration Registration, string[] Patterns);
}
