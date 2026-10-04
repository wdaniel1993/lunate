using System.Text;

namespace Spike.RawKeys;

public sealed record KeyEvent(string Name, char? Char, bool Ctrl, bool Shift, bool Alt, byte[] Raw)
{
    public string Display()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        sb.Append(Name);
        if (Char is { } c) sb.Append($" char='{c}'");
        sb.Append(" raw=");
        foreach (var b in Raw)
        {
            if (b is >= 0x20 and <= 0x7E) sb.Append((char)b);
            else sb.Append($"\\x{b:x2}");
        }
        return sb.ToString();
    }
}
