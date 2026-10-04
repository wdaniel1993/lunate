namespace Spike.RawKeys;

internal static class Headless
{
    public static int Run(Stream input)
    {
        Console.WriteLine("headless: decoding stdin bytes as VT input");
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (KeyDecoder.TryDecode(bytes.AsSpan(offset), out var key, out var consumed))
            {
                Console.WriteLine($"key {key!.Display()}");
                offset += consumed;
            }
            else
            {
                Console.WriteLine($"partial bytes={BitConverter.ToString(bytes, offset).Replace('-', ' ').ToLowerInvariant()}");
                break;
            }
        }
        Console.WriteLine($"decoded_bytes: {offset}/{bytes.Length}");
        return 0;
    }
}
