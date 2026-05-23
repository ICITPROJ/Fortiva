using System.Security.Cryptography;

namespace Fortiva.Core.Otp;

/// <summary>RFC 6238 TOTP (SHA-1, 30 s step, 6 digits — Google Authenticator default).</summary>
public static class TotpGenerator
{
    public const int DefaultPeriodSeconds = 30;
    public const int DefaultDigits = 6;

    public static string Generate(
        string secret,
        DateTimeOffset? time = null,
        int digits = DefaultDigits,
        int periodSeconds = DefaultPeriodSeconds)
    {
        var normalized = TotpSecretNormalizer.Normalize(secret)
            ?? throw new FormatException("TOTP secret is empty.");
        var key = Base32Encoding.Decode(normalized);
        var counter = GetCounter(time ?? DateTimeOffset.UtcNow, periodSeconds);
        return GenerateHotp(key, counter, digits);
    }

    public static int GetRemainingSeconds(DateTimeOffset? time = null, int periodSeconds = DefaultPeriodSeconds)
    {
        var unix = (time ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        return periodSeconds - (int)(unix % periodSeconds);
    }

    public static ulong GetCounter(DateTimeOffset time, int periodSeconds = DefaultPeriodSeconds)
        => (ulong)(time.ToUnixTimeSeconds() / periodSeconds);

    public static string GenerateHotp(byte[] key, ulong counter, int digits)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString($"D{digits}");
    }
}
