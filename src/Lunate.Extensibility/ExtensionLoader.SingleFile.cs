using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly List<SingleFileExtension> _singleFile = [];

    /// <summary>
    /// The single-file candidates discovered beside the packaged extensions, global before project
    /// and each group in file-name order. A refused file stays in the list — <c>/extensions</c>
    /// shows why instead of hiding it — and its id never displaces a packaged extension.
    /// </summary>
    public IReadOnlyList<SingleFileExtension> SingleFileExtensions => [.. _singleFile];

    /// <summary>
    /// Reads both extensions roots for <c>*.cs</c> candidates, warns about what their headers
    /// ignored, then settles the identities: a packaged extension's id wins and the file is
    /// refused, and two files that declare one id are both refused (neither is more trustworthy
    /// than the other). Nothing is restored, compiled or trusted here.
    /// </summary>
    private void DiscoverSingleFileExtensions(string globalRoot, string projectRoot)
    {
        var reader = new SingleFileExtensionReader(_options);
        _singleFile.AddRange(reader.Read(globalRoot, ExtensionScope.Global, _discoveryErrors));
        _singleFile.AddRange(reader.Read(projectRoot, ExtensionScope.Project, _discoveryErrors));

        IExtensionLog log = _options.Log ?? NullExtensionLog.Instance;
        foreach (SingleFileExtension candidate in _singleFile)
        {
            foreach (ExtensionHeaderIssue warning in candidate.Header.Warnings)
            {
                log.Warn($"{candidate.Path}: {warning}");
            }
        }

        SettleSingleFileIdentities();
    }

    /// <summary>
    /// Refuses the candidates whose declared id is already taken. A refusal is a discovery error
    /// naming both paths and an <see cref="SingleFileExtensionState.Invalid"/> candidate, so the
    /// file is listed with the reason rather than dropped.
    /// </summary>
    private void SettleSingleFileIdentities()
    {
        List<int> accepted =
        [
            .. Enumerable
                .Range(0, _singleFile.Count)
                .Where(index => _singleFile[index].Header.IsValid),
        ];

        foreach (int index in accepted)
        {
            SingleFileExtension candidate = _singleFile[index];
            if (_descriptorsById.TryGetValue(candidate.Id!, out ExtensionDescriptor? packaged))
            {
                Refuse(
                    index,
                    $"declared more than once: '{packaged.Directory}' and '{candidate.Path}'"
                );
            }
        }

        IEnumerable<IGrouping<string, int>> duplicates = accepted
            .Where(index => _singleFile[index].State != SingleFileExtensionState.Invalid)
            .GroupBy(index => _singleFile[index].Id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (IGrouping<string, int> group in duplicates)
        {
            string paths = string.Join(
                " and ",
                group.Select(index => $"'{_singleFile[index].Path}'")
            );
            foreach (int index in group)
            {
                Refuse(index, "declared more than once: " + paths);
            }
        }
    }

    private void Refuse(int index, string reason)
    {
        SingleFileExtension candidate = _singleFile[index];
        _singleFile[index] = candidate with
        {
            State = SingleFileExtensionState.Invalid,
            Reason = reason,
        };
        _discoveryErrors.Add(
            new ExtensionDiscoveryError(candidate.Path, $"extension '{candidate.Id}' is {reason}.")
        );
    }
}
