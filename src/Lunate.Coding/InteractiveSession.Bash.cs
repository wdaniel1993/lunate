using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private bool _bashMode;
    private bool _bashSilent;

    /// <summary>Whether the input line is in bash mode (the tests' view).</summary>
    internal bool IsBashMode => _bashMode;

    /// <summary>Whether bash mode is the silent form (the tests' view).</summary>
    internal bool IsBashSilent => _bashSilent;

    /// <summary>
    /// Enters bash mode when the first character of an empty input is <c>!</c>, or flips an
    /// already-entered empty normal form to silent when the next character is a second <c>!</c>.
    /// Returns true when the mode changed and the live input was updated.
    /// </summary>
    private bool TryEnterBashMode(bool wasEmpty, KeyEvent key)
    {
        if (!wasEmpty)
        {
            return false;
        }

        if (!_bashMode)
        {
            if (!_input.Text.StartsWith('!'))
            {
                return false;
            }

            bool silent = _input.Text.StartsWith("!!");
            string command = _input.Text[(silent ? 2 : 1)..];
            _bashMode = true;
            _bashSilent = silent;
            _input = new InputLineState(command, command.Length);
            CloseAutocomplete();
            _live.SetInput(_input, bashMode: true);
            return true;
        }

        if (!_bashSilent && key is { Kind: KeyKind.Character, Text: "!" } && _input.Text == "!")
        {
            _bashSilent = true;
            _input = InputLine.Empty;
            CloseAutocomplete();
            _live.SetInput(_input, bashMode: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Leaves bash mode. Restoring puts the prefix back in front of the typed line, so Esc never
    /// loses it; submitting clears the line.
    /// </summary>
    private void LeaveBashMode(bool restore)
    {
        string prefix = _bashSilent ? "!!" : "!";
        string text = restore ? prefix + _input.Text : string.Empty;
        _bashMode = false;
        _bashSilent = false;
        _input = new InputLineState(text, text.Length);
        _live.SetInput(_input);
    }

    /// <summary>
    /// The bash-mode submission path: Enter means run, and a bare prefix leaves without running.
    /// </summary>
    private Task SubmitBashAsync(CancellationToken ct)
    {
        string command = _input.Text;
        LeaveBashMode(restore: false);
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
