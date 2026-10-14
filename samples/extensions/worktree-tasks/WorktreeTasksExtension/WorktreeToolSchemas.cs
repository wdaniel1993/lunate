using System.Text.Json;

namespace WorktreeTasksExtension;

/// <summary>The parameter schemas of the four worktree tools.</summary>
internal static class WorktreeToolSchemas
{
    public static JsonElement Start { get; } =
        Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "the worktree name (letters, digits, '.', '-' and '_'); the branch is lunate/<name> and the path is a sibling of the root named <root>-<name>"
                },
                "task": {
                  "type": "string",
                  "description": "the task for the subagent that runs in the new worktree"
                },
                "base": {
                  "type": "string",
                  "description": "the ref the worktree branch starts from; defaults to HEAD"
                }
              },
              "required": ["name", "task"],
              "additionalProperties": false
            }
            """
        );

    public static JsonElement Merge { get; } =
        Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "the worktree name whose branch (lunate/<name>) is merged back"
                },
                "into": {
                  "type": "string",
                  "description": "the branch to merge into; defaults to HEAD (the current branch must match)"
                }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """
        );

    public static JsonElement Clean { get; } =
        Parse(
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "the worktree name to remove (and its lunate/<name> branch)"
                },
                "force": {
                  "type": "boolean",
                  "description": "remove a dirty worktree and delete an unmerged branch; defaults to false"
                }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """
        );

    public static JsonElement List { get; } =
        Parse(
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """
        );

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
