using Fortiva.Core.Password;

namespace Fortiva.Core.Tests.Password;

public class PasswordStrengthTests
{
    [Theory]
    [InlineData("", PasswordStrength.VeryWeak)]
    [InlineData("abc", PasswordStrength.VeryWeak)]
    [InlineData("password123", PasswordStrength.Weak)]
    [InlineData("CorrectHorse99", PasswordStrength.Fair)]
    [InlineData("C0rrect-H0rse!2026", PasswordStrength.Strong)]
    [InlineData("C0rrect-H0rse!Battery#Staple-2026-XYZ", PasswordStrength.VeryStrong)]
    public void Analyze_ReturnsExpectedStrength(string password, PasswordStrength expected)
    {
        var result = PasswordStrengthAnalyzer.Analyze(password);
        Assert.True(result.Strength <= expected + 1 && result.Strength >= expected - 1,
            $"Expected ~{expected} for '{password}' but got {result.Strength}");
    }

    [Fact]
    public void Analyze_EntropyIncreasesWith_Length()
    {
        var short16 = PasswordStrengthAnalyzer.Analyze("Aa!1Aa!1Aa!1Aa!1");
        var long32 = PasswordStrengthAnalyzer.Analyze("Aa!1Aa!1Aa!1Aa!1Aa!1Aa!1Aa!1Aa!1");
        Assert.True(long32.EntropyBits > short16.EntropyBits);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("qwerty123")]
    [InlineData("iloveyou")]
    public void Analyze_CommonPatterns_HaveWarnings(string password)
    {
        var result = PasswordStrengthAnalyzer.Analyze(password);
        Assert.True(result.Warnings.Count > 0);
    }

    [Fact]
    public void Generator_ProducesExpectedLengths()
    {
        for (var len = 8; len <= 64; len += 4)
        {
            var pw = PasswordGenerator.Generate(len, PasswordGeneratorMode.AlphanumericSymbols);
            Assert.Equal(len, pw.Length);
        }
    }

    [Fact]
    public void Generator_Passphrase_ContainsHyphenOrDot()
    {
        for (var i = 0; i < 20; i++)
        {
            var pp = PasswordGenerator.GeneratePassphrase(4);
            Assert.True(pp.Contains('-') || pp.Contains('.'), $"Passphrase '{pp}' missing separator");
        }
    }

    [Fact]
    public void Generator_ProducesUnique100Passwords()
    {
        var passwords = Enumerable.Range(0, 100)
            .Select(_ => PasswordGenerator.Generate(20, PasswordGeneratorMode.AlphanumericSymbols))
            .ToHashSet();
        Assert.Equal(100, passwords.Count);
    }
}
