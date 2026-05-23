using Fortiva.Core.Vault;

namespace Fortiva.Core.Password;

public sealed class PasswordHealthReport
{
    public int TotalEntries { get; init; }
    public int WeakCount { get; init; }
    public int ReusedCount { get; init; }
    public int OldCount { get; init; }
    public List<Guid> WeakEntryIds { get; init; } = [];
    public List<Guid> ReusedEntryIds { get; init; } = [];
    public List<Guid> OldEntryIds { get; init; } = [];
}

public static class PasswordHealthAnalyzer
{
    private const int WeakLengthThreshold = 12;
    private static readonly TimeSpan OldPasswordAge = TimeSpan.FromDays(365);

    public static PasswordHealthReport Analyze(IEnumerable<VaultEntry> entries)
    {
        var list = entries.Where(e => !e.IsSecureNote && !string.IsNullOrEmpty(e.Password)).ToList();
        var passwordGroups = list.GroupBy(e => e.Password).Where(g => g.Count() > 1).SelectMany(g => g).Select(e => e.Id).ToHashSet();
        var weak = new List<Guid>();
        var old = new List<Guid>();

        foreach (var entry in list)
        {
            if (IsWeak(entry.Password))
                weak.Add(entry.Id);
            if (DateTimeOffset.UtcNow - entry.ModifiedAt > OldPasswordAge)
                old.Add(entry.Id);
        }

        return new PasswordHealthReport
        {
            TotalEntries = list.Count,
            WeakCount = weak.Count,
            ReusedCount = passwordGroups.Count,
            OldCount = old.Count,
            WeakEntryIds = weak,
            ReusedEntryIds = passwordGroups.ToList(),
            OldEntryIds = old
        };
    }

    private static bool IsWeak(string password)
    {
        if (password.Length < WeakLengthThreshold) return true;
        var hasLower = password.Any(char.IsLower);
        var hasUpper = password.Any(char.IsUpper);
        var hasDigit = password.Any(char.IsDigit);
        var hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));
        var classes = new[] { hasLower, hasUpper, hasDigit, hasSymbol }.Count(x => x);
        return classes < 3;
    }
}
