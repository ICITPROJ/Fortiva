using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public class NativeMessagingFramingTests
{
    [Fact]
    public void CreateLengthPrefix_UsesLittleEndian()
    {
        var header = NativeMessagingFraming.CreateLengthPrefix(0x00010203);

        if (BitConverter.IsLittleEndian)
        {
            Assert.Equal(0x03, header[0]);
            Assert.Equal(0x02, header[1]);
            Assert.Equal(0x01, header[2]);
            Assert.Equal(0x00, header[3]);
        }
        else
        {
            Assert.Equal(0x00, header[0]);
            Assert.Equal(0x01, header[1]);
            Assert.Equal(0x02, header[2]);
            Assert.Equal(0x03, header[3]);
        }
    }

    [Fact]
    public void WriteLengthPrefixedMessage_WritesHeaderThenPayload()
    {
        using var stream = new MemoryStream();
        var payload = "{\"ok\":true}"u8.ToArray();

        NativeMessagingFraming.WriteLengthPrefixedMessage(stream, payload);

        var bytes = stream.ToArray();
        Assert.Equal(payload.Length, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(payload, bytes.AsSpan(4).ToArray());
    }
}
