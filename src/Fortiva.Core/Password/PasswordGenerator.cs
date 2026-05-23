using System.Security.Cryptography;
using System.Text;

namespace Fortiva.Core.Password;

public enum PasswordGeneratorMode
{
    Alphanumeric,
    AlphanumericSymbols,
    Passphrase,
    Pin,
    Custom
}

/// <summary>User-editable password generation settings.</summary>
public sealed class PasswordGeneratorOptions
{
    public const string DefaultSymbols = "!@#$%^&*()-_=+[]{}|;:,.<>?";

    public PasswordGeneratorMode Mode { get; set; } = PasswordGeneratorMode.AlphanumericSymbols;
    public int Length { get; set; } = 20;
    public int PassphraseWordCount { get; set; } = 4;
    public string PassphraseSeparator { get; set; } = "-";
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
    public string CustomSymbols { get; set; } = DefaultSymbols;
    /// <summary>When set, only these characters are used (Custom mode).</summary>
    public string? CustomCharset { get; set; }
    public bool ExcludeAmbiguous { get; set; } = true;
    /// <summary>Guarantee at least one character from each enabled group.</summary>
    public bool RequireFromEachGroup { get; set; } = true;

    public static PasswordGeneratorOptions Default => new();

    public PasswordGeneratorOptions Clone() => new()
    {
        Mode = Mode,
        Length = Length,
        PassphraseWordCount = PassphraseWordCount,
        PassphraseSeparator = PassphraseSeparator,
        IncludeLowercase = IncludeLowercase,
        IncludeUppercase = IncludeUppercase,
        IncludeDigits = IncludeDigits,
        IncludeSymbols = IncludeSymbols,
        CustomSymbols = CustomSymbols,
        CustomCharset = CustomCharset,
        ExcludeAmbiguous = ExcludeAmbiguous,
        RequireFromEachGroup = RequireFromEachGroup
    };
}

public static class PasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";

    private static readonly string[] WordList =
    [
        "amber", "bridge", "coral", "delta", "ember", "frost", "glide", "harbor",
        "ivory", "jade", "kite", "lunar", "meadow", "nova", "orbit", "prism",
        "quartz", "river", "storm", "terra", "ultra", "vivid", "willow", "zenith"
    ];

    public static string Generate(int length, PasswordGeneratorMode mode, bool excludeAmbiguous = true)
        => Generate(new PasswordGeneratorOptions
        {
            Mode = mode,
            Length = length,
            ExcludeAmbiguous = excludeAmbiguous,
            RequireFromEachGroup = mode is not PasswordGeneratorMode.Pin and not PasswordGeneratorMode.Passphrase
        });

    public static string Generate(PasswordGeneratorOptions options)
    {
        return options.Mode switch
        {
            PasswordGeneratorMode.Passphrase => GeneratePassphrase(
                Math.Clamp(options.PassphraseWordCount, 2, 12),
                options.PassphraseSeparator),
            PasswordGeneratorMode.Pin => GenerateFromCharset(
                Math.Clamp(options.Length, 4, 16), Digits, excludeAmbiguous: false),
            PasswordGeneratorMode.Custom when !string.IsNullOrWhiteSpace(options.CustomCharset) =>
                GenerateFromCharset(
                    Math.Clamp(options.Length, 4, 128),
                    options.CustomCharset!,
                    excludeAmbiguous: false),
            _ => GenerateFromGroups(options)
        };
    }

    private static string GenerateFromGroups(PasswordGeneratorOptions options)
    {
        var length = Math.Clamp(options.Length, 4, 128);
        var groups = BuildGroups(options);
        if (groups.Count == 0)
            throw new ArgumentException("Enable at least one character type or provide a custom charset.");

        var charset = string.Concat(groups.Select(g => g.Chars));
        charset = ApplyAmbiguousFilter(charset, options.ExcludeAmbiguous);
        if (charset.Length == 0)
            throw new ArgumentException("Character set is empty after applying filters.");

        if (!options.RequireFromEachGroup || groups.Count == 1)
            return GenerateFromCharset(length, charset, excludeAmbiguous: false);

        if (length < groups.Count)
            throw new ArgumentException($"Length must be at least {groups.Count} when requiring each character type.");

        var chars = new List<char>(length);
        foreach (var group in groups)
        {
            var filtered = ApplyAmbiguousFilter(group.Chars, options.ExcludeAmbiguous);
            if (filtered.Length == 0)
                throw new ArgumentException($"Group '{group.Name}' has no characters after filters.");
            chars.Add(PickRandomChar(filtered));
        }

        while (chars.Count < length)
            chars.Add(PickRandomChar(charset));

        Shuffle(chars);
        return new string(chars.ToArray());
    }

    private static List<(string Name, string Chars)> BuildGroups(PasswordGeneratorOptions options)
    {
        var groups = new List<(string, string)>();
        if (options.IncludeLowercase) groups.Add(("lower", Lower));
        if (options.IncludeUppercase) groups.Add(("upper", Upper));
        if (options.IncludeDigits) groups.Add(("digit", Digits));
        if (options.IncludeSymbols)
        {
            var sym = string.IsNullOrEmpty(options.CustomSymbols)
                ? PasswordGeneratorOptions.DefaultSymbols
                : options.CustomSymbols;
            groups.Add(("symbol", sym));
        }
        return groups;
    }

    public static string GenerateFromCharset(int length, string charset, bool excludeAmbiguous = false)
    {
        charset = ApplyAmbiguousFilter(charset, excludeAmbiguous);
        if (length <= 0 || charset.Length == 0)
            throw new ArgumentException("Invalid generation parameters.");

        var result = new StringBuilder(length);
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < length; i++)
            result.Append(charset[bytes[i] % charset.Length]);
        return result.ToString();
    }

    public static string GeneratePassphrase(int wordCount, string separator = "-")
    {
        wordCount = Math.Clamp(wordCount, 2, 12);
        separator = string.IsNullOrEmpty(separator) ? "-" : separator[..Math.Min(separator.Length, 3)];

        var words = new List<string>();
        for (var i = 0; i < wordCount; i++)
            words.Add(WordList[RandomNumberGenerator.GetInt32(0, WordList.Length)]);

        return string.Join(separator, words) + RandomNumberGenerator.GetInt32(10, 99);
    }

    private static string ApplyAmbiguousFilter(string charset, bool excludeAmbiguous)
    {
        if (!excludeAmbiguous) return charset;
        return charset.Replace("0", "").Replace("O", "").Replace("o", "")
            .Replace("l", "").Replace("1", "").Replace("I", "");
    }

    private static char PickRandomChar(string charset)
        => charset[RandomNumberGenerator.GetInt32(0, charset.Length)];

    private static void Shuffle(List<char> chars)
    {
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(0, i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
