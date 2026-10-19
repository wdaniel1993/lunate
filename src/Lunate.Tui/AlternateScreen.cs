namespace Lunate.Tui;

/// <summary>
/// The alternate screen's VT sequences and the scope that restores the primary screen: entering
/// writes the enter sequence, and disposing the returned scope writes the leave sequence exactly
/// once — so an explicit close, a session that ends, and an exception all restore the terminal.
/// </summary>
internal static class AlternateScreen
{
    internal const string EnterSequence = "\u001b[?1049h";
    internal const string LeaveSequence = "\u001b[?1049l";

    /// <summary>Enters the alternate screen through <paramref name="write"/>.</summary>
    /// <returns>The scope whose disposal leaves the alternate screen; disposal is idempotent.</returns>
    public static IDisposable Enter(Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        write(EnterSequence);
        return new Scope(write);
    }

    private sealed class Scope(Action<string> write) : IDisposable
    {
        private bool _left;

        public void Dispose()
        {
            if (_left)
            {
                return;
            }

            _left = true;
            write(LeaveSequence);
        }
    }
}
