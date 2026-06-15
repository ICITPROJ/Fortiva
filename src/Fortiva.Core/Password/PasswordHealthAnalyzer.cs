using Fortiva.Core.Vault;

namespace Fortiva.Core.Password;

public sealed class HealthRecommendation
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Priority { get; init; }
    public int AffectedCount { get; init; }
}

public sealed class PasswordHealthReport
{
    public int TotalEntries { get; init; }
    public int WeakCount { get; init; }
    public int ReusedCount { get; init; }
    public int OldCount { get; init; }
    public int MissingCount { get; init; }
    public int SecureCount { get; init; }
    public int SecurityScore { get; init; }
    public int VeryWeakCount { get; init; }
    public int WeakStrengthCount { get; init; }
    public int FairCount { get; init; }
    public int StrongCount { get; init; }
    public int VeryStrongCount { get; init; }
    public List<Guid> WeakEntryIds { get; init; } = [];
    public List<Guid> ReusedEntryIds { get; init; } = [];
    public List<Guid> OldEntryIds { get; init; } = [];
    public List<Guid> MissingEntryIds { get; init; } = [];
    public List<HealthRecommendation> Recommendations { get; init; } = [];
}

public static class PasswordHealthAnalyzer
{
    private static readonly TimeSpan OldPasswordAge = TimeSpan.FromDays(365);

    public static PasswordHealthReport Analyze(IEnumerable<VaultEntry> entries)
    {
        var all = entries.Where(e => !e.IsSecureNote).ToList();
        var withPassword = all.Where(e => !string.IsNullOrEmpty(e.Password)).ToList();
        var missing = all.Where(e => string.IsNullOrEmpty(e.Password)).Select(e => e.Id).ToList();

        var reusedIds = withPassword
            .GroupBy(e => e.Password)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .Select(e => e.Id)
            .ToHashSet();

        var weak = new List<Guid>();
        var old = new List<Guid>();
        var veryWeak = 0;
        var weakOnly = 0;
        var fair = 0;
        var strong = 0;
        var veryStrong = 0;
        var secure = 0;

        foreach (var entry in withPassword)
        {
            var strength = PasswordStrengthAnalyzer.Analyze(entry.Password).Strength;
            var isWeak = strength <= PasswordStrength.Weak;
            var isOld = DateTimeOffset.UtcNow - entry.ModifiedAt > OldPasswordAge;
            var isReused = reusedIds.Contains(entry.Id);

            switch (strength)
            {
                case PasswordStrength.VeryWeak: veryWeak++; break;
                case PasswordStrength.Weak: weakOnly++; break;
                case PasswordStrength.Fair: fair++; break;
                case PasswordStrength.Strong: strong++; break;
                default: veryStrong++; break;
            }

            if (isWeak)
                weak.Add(entry.Id);
            if (isOld)
                old.Add(entry.Id);
            if (!isWeak && !isReused && !isOld)
                secure++;
        }

        var loginTotal = all.Count;
        var score = ComputeScore(loginTotal, secure, weak.Count, reusedIds.Count, old.Count, missing.Count);
        var recommendations = BuildRecommendations(
            score, weak.Count, reusedIds.Count, old.Count, missing.Count, loginTotal);

        return new PasswordHealthReport
        {
            TotalEntries = withPassword.Count,
            WeakCount = weak.Count,
            ReusedCount = reusedIds.Count,
            OldCount = old.Count,
            MissingCount = missing.Count,
            SecureCount = secure,
            SecurityScore = score,
            VeryWeakCount = veryWeak,
            WeakStrengthCount = weakOnly,
            FairCount = fair,
            StrongCount = strong,
            VeryStrongCount = veryStrong,
            WeakEntryIds = weak,
            ReusedEntryIds = reusedIds.ToList(),
            OldEntryIds = old,
            MissingEntryIds = missing,
            Recommendations = recommendations
        };
    }

    internal static int ComputeScore(int loginTotal, int secure, int weak, int reused, int old, int missing)
    {
        if (loginTotal == 0)
            return 100;

        var baseScore = (int)Math.Round(secure * 100.0 / loginTotal);
        var penalty = Math.Min(45, reused * 6 + old * 3 + missing * 8 + weak * 2);
        return Math.Max(0, Math.Min(100, baseScore - penalty / Math.Max(1, loginTotal / 3)));
    }

    private static List<HealthRecommendation> BuildRecommendations(
        int score, int weak, int reused, int old, int missing, int loginTotal)
    {
        var items = new List<HealthRecommendation>();

        if (reused > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "reused",
                Title = $"Use unique passwords on {reused} account{(reused == 1 ? "" : "s")}",
                Detail = "Reused passwords mean one breach can unlock multiple sites. Generate a unique password for each login.",
                Priority = 1,
                AffectedCount = reused
            });
        }

        if (weak > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "weak",
                Title = $"Strengthen {weak} weak password{(weak == 1 ? "" : "s")}",
                Detail = "Short or predictable passwords are easy to guess. Use 16+ characters with mixed letters, numbers, and symbols.",
                Priority = 2,
                AffectedCount = weak
            });
        }

        if (old > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "old",
                Title = $"Rotate {old} password{(old == 1 ? "" : "s")} older than a year",
                Detail = "Start with banking, email, and work accounts - then update the rest over time.",
                Priority = 3,
                AffectedCount = old
            });
        }

        if (missing > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "missing",
                Title = $"Add passwords to {missing} incomplete entr{(missing == 1 ? "y" : "ies")}",
                Detail = "Entries without passwords cannot autofill or protect those accounts.",
                Priority = 4,
                AffectedCount = missing
            });
        }

        if (items.Count == 0 && loginTotal > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "great",
                Title = "Your vault looks strong",
                Detail = "Keep unique passwords, export an encrypted backup occasionally, and enable Windows Hello for faster unlock.",
                Priority = 10,
                AffectedCount = 0
            });
        }
        else if (loginTotal == 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "start",
                Title = "Add your first logins",
                Detail = "Save accounts in your vault, then return here for a full security score and personalized tips.",
                Priority = 10,
                AffectedCount = 0
            });
        }
        else if (score >= 70 && items.Count > 0)
        {
            items.Add(new HealthRecommendation
            {
                Id = "backup",
                Title = "Export an encrypted backup",
                Detail = "Save a .fva backup to cloud storage so you can restore if you change PCs.",
                Priority = 9,
                AffectedCount = 0
            });
        }

        return items.OrderBy(r => r.Priority).ToList();
    }
}
