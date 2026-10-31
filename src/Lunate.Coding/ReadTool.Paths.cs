using Lunate.Agent;

namespace Lunate.Coding;

public sealed partial class ReadTool
{
    /// <summary>
    /// Resolves a path outside the workspace against the run's skill allowance or its exact known
    /// spill files.
    /// </summary>
    private bool TryResolveAllowed(string path, Session? session, out ResolvedPath resolved)
    {
        resolved = null!;
        if ((_allowance is null || _allowance.Roots.Count == 0) && session is null)
        {
            return false;
        }

        string canonical;
        try
        {
            canonical = Workspace.Canonicalize(
                Path.IsPathRooted(path) ? path : Path.Combine(_workspace.WorktreeRoot, path)
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
            return false;
        }

        if (
            !(_allowance?.Contains(canonical) ?? false)
            && !(session is not null && ToolOutputSpill.IsKnownFile(session, canonical))
        )
        {
            return false;
        }

        resolved = new ResolvedPath(
            canonical,
            SkillPaths.Display(canonical, _workspace.WorktreeRoot)
        );
        return true;
    }
}
