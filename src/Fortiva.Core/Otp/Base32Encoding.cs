namespace Fortiva.Core.Otp;

/// <summary>RFC 4648 Base32 decoder for authenticator shared secrets.</summary>
public static class Base32Encoding
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("TOTP secret is empty.");

        input = input.Trim().TrimEnd('=').Replace(" ", "").Replace("-", "");
        if (input.Length == 0)
            throw new FormatException("TOTP secret is empty.");

        var output = new List<byte>(input.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in input.ToUpperInvariant())
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Invalid Base32 character: {c}");

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)(buffer >> bitsLeft));
                buffer &= (1 << bitsLeft) - 1;
            }
        }

        return output.ToArray();
    }
}
