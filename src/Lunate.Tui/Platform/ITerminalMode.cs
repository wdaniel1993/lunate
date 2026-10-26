namespace Lunate.Tui.Platform;

/// <summary>
/// The terminal's input mode as the process found it, captured when raw mode is entered. Suspending
/// puts the terminal back into that mode for a child that needs a cooked terminal; resuming applies
/// the raw mode again. Disposing restores the original mode for good.
/// </summary>
internal interface ITerminalMode : IDisposable
{
    /// <summary>Returns the terminal to the mode the process started with (cooked).</summary>
    void Suspend();

    /// <summary>Applies raw mode again after <see cref="Suspend"/>.</summary>
    void Resume();
}
