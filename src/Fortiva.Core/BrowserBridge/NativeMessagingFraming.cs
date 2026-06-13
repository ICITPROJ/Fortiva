namespace Fortiva.Core.BrowserBridge;

/// <summary>Chromium native messaging length-prefixed framing (32-bit little-endian).</summary>
public static class NativeMessagingFraming
{
    public static byte[] CreateLengthPrefix(int payloadLength)
    {
        var header = BitConverter.GetBytes(payloadLength);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(header);
        return header;
    }

    public static void WriteLengthPrefixedMessage(Stream stdout, ReadOnlySpan<byte> payload)
    {
        var header = CreateLengthPrefix(payload.Length);
        stdout.Write(header, 0, 4);
        stdout.Write(payload);
        stdout.Flush();
    }
}
