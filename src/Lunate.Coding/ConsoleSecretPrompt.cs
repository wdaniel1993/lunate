using System.Text;

namespace Lunate.Coding;

/// <summary>
/// The console masked prompt behind <c>lunate auth login</c> when stdin is a terminal. Each printable
/// key appends to a private buffer and writes one <c>*</c>; Backspace removes, Enter returns the
/// value, Escape and Ctrl+C cancel. The typed character is never written, and the key reader is
/// injectable so the masking is testable without a terminal.
/// </summary>
internal sealed class ConsoleSecretPrompt(TextWriter output, Func<ConsoleKeyInfo>? readKey = null)
    : ISecretPrompt
{
    public Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<string?>(null);
        }

        output.Write($"{prompt}: ");
        output.Flush();

        Func<ConsoleKeyInfo> read = readKey ?? (() => Console.ReadKey(intercept: true));
        var buffer = new StringBuilder();
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                output.WriteLine();
                return Task.FromResult<string?>(null);
            }

            ConsoleKeyInfo key = read();
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return Task.FromResult<string?>(buffer.ToString());
            }

            if (
                key.Key == ConsoleKey.Escape
                || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            )
            {
                output.WriteLine();
                return Task.FromResult<string?>(null);
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    output.Write("\b \b");
                    output.Flush();
                }

                continue;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                output.Write('*');
                output.Flush();
            }
        }
    }
}
