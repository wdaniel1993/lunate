using System.ComponentModel;
using System.Diagnostics;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>Where the draft file goes; a test pins an isolated directory, production the temp dir.</summary>
    private string EditorTempDirectory => _options.EditorTempDirectory ?? Path.GetTempPath();

    /// <summary>
    /// <c>Ctrl+G</c>: writes the draft to a temporary file, hands the terminal to the editor, and
    /// puts the result back. The child owns the terminal for its whole life — the key pump is
    /// quiesced and the live area suspended — so a key typed at the editor belongs to the editor.
    /// A failure leaves the draft, the cursor and the stash exactly as they were and posts one
    /// notice naming what failed; the file is deleted on every path.
    /// </summary>
    private async Task RunEditorAsync(CancellationToken ct)
    {
        CloseAutocomplete();
        InputLineState before = _input;
        bool windows = OperatingSystem.IsWindows();
        EditorCommand command = DraftEditor.Resolve(_environment, windows);
        string path = Path.Combine(EditorTempDirectory, "lunate-draft-" + Path.GetRandomFileName());

        try
        {
            if (DraftEditor.Locate(command.Program, _environment, windows) is null)
            {
                Notify(DraftEditor.MissingProgramNotice(command));
                return;
            }

            DraftEditor.WriteDraft(path, before.Text);
            string failure;
            using (_console.SuspendTerminal())
            {
                _live.SetSuspended(true);
                try
                {
                    failure = await RunEditorChildAsync(command, path, ct).ConfigureAwait(false);
                }
                finally
                {
                    _live.SetSuspended(false);
                }
            }

            if (failure.Length > 0)
            {
                // Delete before the notice: a caller who reads the notice and looks at the
                // directory must not find the draft the notice says was not kept.
                DraftEditor.TryDelete(path);
                Notify(failure);
                return;
            }

            string edited;
            try
            {
                edited = DraftEditor.ReadBack(File.ReadAllText(path));
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                DraftEditor.TryDelete(path);
                Notify(DraftEditor.ReadFailureNotice(exception));
                return;
            }

            ApplyEditedDraft(before.Text, edited);
        }
        finally
        {
            DraftEditor.TryDelete(path);
        }
    }

    /// <summary>
    /// Runs the child with the session's own handles, so it draws on the terminal directly. Returns
    /// the failure notice, or the empty string when the editor exited 0.
    /// </summary>
    private async Task<string> RunEditorChildAsync(
        EditorCommand command,
        string path,
        CancellationToken ct
    )
    {
        try
        {
            using Process child = DraftEditor.Start(command, path, _workspace.WorktreeRoot);
            await child.WaitForExitAsync(ct).ConfigureAwait(false);
            return child.ExitCode == 0 ? string.Empty : DraftEditor.ExitNotice(child.ExitCode);
        }
        catch (Win32Exception exception)
        {
            return DraftEditor.StartFailureNotice(command, exception);
        }
    }

    /// <summary>
    /// The editor's text becomes the prompt, with the cursor at its end: the old offset means
    /// nothing in text rewritten in another program. A draft that changed while the editor was
    /// open goes to the stash instead of being lost — the editor is the deliberate act, so its
    /// text wins, and the other text stays reachable (design D5).
    /// </summary>
    private void ApplyEditedDraft(string draftWhenOpened, string edited)
    {
        string meanwhile = _input.Text;
        if (!string.Equals(meanwhile, draftWhenOpened, StringComparison.Ordinal))
        {
            _stashedDraft = meanwhile;
            Notify(
                "the draft changed while the editor was open \u2014 the editor's text wins; "
                    + "the earlier draft went to the stash (Ctrl+S restores it)"
            );
        }

        _input = new InputLineState(edited, edited.Length);
        _historyDraft = null;
        _live.SetInput(_input, _bashMode);
    }
}
