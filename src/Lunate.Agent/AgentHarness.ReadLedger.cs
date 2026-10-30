using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private void SeedReadLedger(IEnumerable<ChatMessage> history)
    {
        foreach (
            FunctionCallContent call in history
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
        )
        {
            if (
                call.Name != "read"
                || string.IsNullOrEmpty(call.CallId)
                || ReadPath(call) is not { } path
            )
            {
                continue;
            }

            try
            {
                string fullPath = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(_options.WorkingDirectory, path);
                _reads.Seed(
                    GitIdentity.Canonicalize(fullPath),
                    call.CallId,
                    path.Replace('\\', '/')
                );
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                // An unresolvable historical path cannot identify a file to protect.
            }
        }
    }

    private void StubStaleReadResults()
    {
        IReadOnlyDictionary<string, string> notices = _reads.StaleReadNotices;
        if (notices.Count == 0)
        {
            return;
        }

        for (int index = 0; index < _history.Count; index++)
        {
            ChatMessage message = _history[index];
            if (message.Role != ChatRole.Tool)
            {
                continue;
            }

            bool changed = false;
            AIContent[] contents =
            [
                .. message.Contents.Select(content =>
                {
                    if (
                        content is FunctionResultContent result
                        && notices.TryGetValue(result.CallId, out string? notice)
                        && !string.Equals(
                            result.Result?.ToString(),
                            notice,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        changed = true;
                        return new FunctionResultContent(result.CallId, notice);
                    }

                    return content;
                }),
            ];
            if (!changed)
            {
                continue;
            }

            ChatMessage replacement = message.Clone();
            replacement.Contents = contents;
            _historyUtf8 +=
                CompactionReducer.Utf8Length(replacement) - CompactionReducer.Utf8Length(message);
            _history[index] = replacement;
        }
    }

    private static string? ReadPath(FunctionCallContent call)
    {
        if (
            call.Arguments is not { } arguments
            || !arguments.TryGetValue("path", out object? value)
        )
        {
            return null;
        }

        return value switch
        {
            string path => path,
            System.Text.Json.JsonElement
            {
                ValueKind: System.Text.Json.JsonValueKind.String
            } element => element.GetString(),
            _ => null,
        };
    }
}
