using System.Text;
using Fortiva.Core.Otp;

namespace Fortiva.Core.Tests.Otp;

public sealed class TotpGeneratorTests
{
    [Fact]
    public void Hotp_ascii_secret_counter_one_matches_RFC6238()
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("287082", TotpGenerator.GenerateHotp(key, 1, 6));
    }

    [Fact]
    public void Normalize_accepts_otpauth_uri()
    {
        var normalized = TotpSecretNormalizer.Normalize(
            "otpauth://totp/Fortiva:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Fortiva");
        Assert.Equal("JBSWY3DPEHPK3PXP", normalized);
    }

    [Fact]
    public void Generate_produces_six_digit_code()
    {
        var code = TotpGenerator.Generate("JBSWY3DPEHPK3PXP");
        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsDigit));
    }

    [Fact]
    public void Generate_changes_after_period()
    {
        var t1 = DateTimeOffset.FromUnixTimeSeconds(0);
        var t2 = DateTimeOffset.FromUnixTimeSeconds(30);
        Assert.NotEqual(
            TotpGenerator.Generate("JBSWY3DPEHPK3PXP", t1),
            TotpGenerator.Generate("JBSWY3DPEHPK3PXP", t2));
    }

    [Fact]
    public void GetRemainingSeconds_counts_down_within_period()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(95);
        Assert.Equal(25, TotpGenerator.GetRemainingSeconds(time));
    }
}
