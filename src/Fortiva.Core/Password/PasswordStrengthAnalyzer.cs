using System.Text.RegularExpressions;

namespace Fortiva.Core.Password;

public enum PasswordStrength
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
    VeryStrong = 4
}

public sealed class PasswordStrengthResult
{
    public PasswordStrength Strength { get; init; }
    public int Score { get; init; }
    public double EntropyBits { get; init; }
    public string Label { get; init; } = "";
    public List<string> Warnings { get; init; } = [];
    public List<string> Suggestions { get; init; } = [];
}

public static partial class PasswordStrengthAnalyzer
{
    private static readonly string[] CommonWords =
    [
        "password", "qwerty", "abc123", "letmein", "monkey", "iloveyou",
        "admin", "welcome", "login", "master", "dragon", "sunshine"
    ];

    private static readonly string[] KeyboardWalks =
    [
        "qwerty", "asdf", "zxcv", "qwertyuiop", "asdfghjkl", "zxcvbnm",
        "1234", "12345", "123456", "1234567", "12345678", "123456789"
    ];

    public static PasswordStrengthResult Analyze(string password)
    {
        if (string.IsNullOrEmpty(password))
            return new PasswordStrengthResult
            {
                Strength = PasswordStrength.VeryWeak,
                Score = 0,
                Label = "Very Weak",
                Warnings = ["Password is empty."]
            };

        var warnings = new List<string>();
        var suggestions = new List<string>();
        var score = 0;

        // ── Character class coverage ─────────────────────────────────────────
        var hasLower = LowerRegex().IsMatch(password);
        var hasUpper = UpperRegex().IsMatch(password);
        var hasDigit = DigitRegex().IsMatch(password);
        var hasSymbol = SymbolRegex().IsMatch(password);
        var classCount = new[] { hasLower, hasUpper, hasDigit, hasSymbol }.Count(x => x);

        // ── Entropy estimate ─────────────────────────────────────────────────
        var pool = 0;
        if (hasLower) pool += 26;
        if (hasUpper) pool += 26;
        if (hasDigit) pool += 10;
        if (hasSymbol) pool += 32;
        if (pool == 0) pool = 26;
        var entropy = password.Length * Math.Log2(pool);

        // ── Length scoring ───────────────────────────────────────────────────
        score += password.Length switch
        {
            < 8 => 0,
            < 12 => 1,
            < 16 => 2,
            < 20 => 3,
            _ => 4
        };

        if (password.Length < 8)
            suggestions.Add("Use at least 8 characters.");
        else if (password.Length < 12)
            suggestions.Add("Aim for 12 or more characters.");

        // ── Class diversity ──────────────────────────────────────────────────
        score += classCount switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 };

        if (!hasUpper) suggestions.Add("Add uppercase letters.");
        if (!hasDigit) suggestions.Add("Add numbers.");
        if (!hasSymbol) suggestions.Add("Add special characters (!, @, #…).");

        // ── Pattern penalties ────────────────────────────────────────────────
        var lower = password.ToLowerInvariant();

        foreach (var word in CommonWords)
        {
            if (lower.Contains(word))
            {
                score -= 2;
                warnings.Add($"Contains a common pattern: \"{word}\".");
                break;
            }
        }

        foreach (var walk in KeyboardWalks)
        {
            if (lower.Contains(walk))
            {
                score -= 2;
                warnings.Add("Contains a keyboard sequence.");
                break;
            }
        }

        // Repeating characters (aaaa, 1111)
        if (RepeatRegex().IsMatch(password))
        {
            score -= 1;
            warnings.Add("Contains repeated characters.");
        }

        // Purely numeric
        if (NumericOnlyRegex().IsMatch(password))
        {
            score -= 1;
            warnings.Add("Purely numeric passwords are weak.");
        }

        score = Math.Max(0, Math.Min(8, score));

        var strength = score switch
        {
            <= 1 => PasswordStrength.VeryWeak,
            <= 3 => PasswordStrength.Weak,
            <= 5 => PasswordStrength.Fair,
            <= 7 => PasswordStrength.Strong,
            _ => PasswordStrength.VeryStrong
        };

        var label = strength switch
        {
            PasswordStrength.VeryWeak => "Very Weak",
            PasswordStrength.Weak => "Weak",
            PasswordStrength.Fair => "Fair",
            PasswordStrength.Strong => "Strong",
            _ => "Very Strong"
        };

        if (suggestions.Count == 0 && strength >= PasswordStrength.Strong)
            suggestions.Add("Great password! Consider using a passphrase for even higher security.");

        return new PasswordStrengthResult
        {
            Strength = strength,
            Score = score,
            EntropyBits = entropy,
            Label = label,
            Warnings = warnings,
            Suggestions = suggestions
        };
    }

    [GeneratedRegex(@"[a-z]")] private static partial Regex LowerRegex();
    [GeneratedRegex(@"[A-Z]")] private static partial Regex UpperRegex();
    [GeneratedRegex(@"[0-9]")] private static partial Regex DigitRegex();
    [GeneratedRegex(@"[^a-zA-Z0-9]")] private static partial Regex SymbolRegex();
    [GeneratedRegex(@"(.)\1{3,}")] private static partial Regex RepeatRegex();
    [GeneratedRegex(@"^\d+$")] private static partial Regex NumericOnlyRegex();
}
