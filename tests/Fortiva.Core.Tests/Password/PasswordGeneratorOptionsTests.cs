using Fortiva.Core.Password;

namespace Fortiva.Core.Tests.Password;

public class PasswordGeneratorOptionsTests
{
    [Fact]
    public void CustomCharset_uses_only_allowed_characters()
    {
        var pw = PasswordGenerator.Generate(new PasswordGeneratorOptions
        {
            Mode = PasswordGeneratorMode.Custom,
            CustomCharset = "abc123",
            Length = 40,
            RequireFromEachGroup = false
        });
        Assert.Equal(40, pw.Length);
        Assert.All(pw, c => Assert.Contains(c, "abc123"));
    }

    [Fact]
    public void RequireFromEachGroup_includes_all_enabled_types()
    {
        var pw = PasswordGenerator.Generate(new PasswordGeneratorOptions
        {
            Length = 16,
            IncludeLowercase = true,
            IncludeUppercase = true,
            IncludeDigits = true,
            IncludeSymbols = true,
            RequireFromEachGroup = true,
            ExcludeAmbiguous = false
        });
        Assert.Equal(16, pw.Length);
        Assert.Contains(pw, c => char.IsLower(c));
        Assert.Contains(pw, c => char.IsUpper(c));
        Assert.Contains(pw, c => char.IsDigit(c));
        Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void CustomSymbols_are_honored()
    {
        for (var i = 0; i < 40; i++)
        {
            var pw = PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Length = 24,
                IncludeLowercase = false,
                IncludeUppercase = false,
                IncludeDigits = true,
                IncludeSymbols = true,
                CustomSymbols = "@#",
                RequireFromEachGroup = true,
                ExcludeAmbiguous = false
            });
            Assert.All(pw, c => Assert.True(char.IsDigit(c) || c is '@' or '#'));
            Assert.Contains(pw, c => c is '@' or '#');
            Assert.DoesNotContain('!', pw);
        }
    }

    [Fact]
    public void Passphrase_respects_word_count_and_separator()
    {
        var pw = PasswordGenerator.Generate(new PasswordGeneratorOptions
        {
            Mode = PasswordGeneratorMode.Passphrase,
            PassphraseWordCount = 5,
            PassphraseSeparator = "_"
        });
        var parts = pw[..^2].Split('_');
        Assert.Equal(5, parts.Length);
    }

    [Fact]
    public void ExcludeAmbiguous_removes_confusing_characters()
    {
        for (var i = 0; i < 50; i++)
        {
            var pw = PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Length = 32,
                ExcludeAmbiguous = true,
                RequireFromEachGroup = false
            });
            Assert.DoesNotContain('0', pw);
            Assert.DoesNotContain('O', pw);
            Assert.DoesNotContain('l', pw);
            Assert.DoesNotContain('1', pw);
        }
    }
}
