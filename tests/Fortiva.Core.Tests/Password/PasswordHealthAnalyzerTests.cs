using Fortiva.Core.Password;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Password;

public class PasswordHealthAnalyzerTests
{
    private static VaultEntry Entry(string title, string password, DateTimeOffset? modified = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Username = "user",
            Password = password,
            ModifiedAt = modified ?? DateTimeOffset.UtcNow
        };

    [Fact]
    public void Analyze_EmptyVault_ReturnsPerfectScore()
    {
        var report = PasswordHealthAnalyzer.Analyze([]);
        Assert.Equal(100, report.SecurityScore);
        Assert.Equal(0, report.TotalEntries);
        Assert.Single(report.Recommendations);
        Assert.Equal("start", report.Recommendations[0].Id);
    }

    [Fact]
    public void Analyze_StrongUniquePasswords_ScoresHigh()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => Entry($"Site{i}", $"C0rrect-H0rse!Battery#{i}-2026"))
            .ToList();

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.Equal(5, report.TotalEntries);
        Assert.Equal(5, report.SecureCount);
        Assert.Equal(0, report.WeakCount);
        Assert.Equal(0, report.ReusedCount);
        Assert.True(report.SecurityScore >= 90);
    }

    [Fact]
    public void Analyze_DetectsReusedAndWeakPasswords()
    {
        var shared = "password123";
        var entries = new[]
        {
            Entry("A", shared),
            Entry("B", shared),
            Entry("C", "C0rrect-H0rse!2026-XYZ")
        };

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.Equal(2, report.ReusedCount);
        Assert.Equal(2, report.WeakCount);
        Assert.Contains(report.Recommendations, r => r.Id == "reused");
        Assert.Contains(report.Recommendations, r => r.Id == "weak");
        Assert.True(report.SecurityScore < 75);
    }

    [Fact]
    public void Analyze_DetectsOldAndMissingPasswords()
    {
        var oldDate = DateTimeOffset.UtcNow.AddDays(-400);
        var entries = new[]
        {
            Entry("Old", "C0rrect-H0rse!Battery#Old-2026", oldDate),
            new VaultEntry
            {
                Id = Guid.NewGuid(),
                Title = "Incomplete",
                Username = "user",
                Password = "",
                ModifiedAt = DateTimeOffset.UtcNow
            }
        };

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.Equal(1, report.OldCount);
        Assert.Equal(1, report.MissingCount);
        Assert.Contains(report.Recommendations, r => r.Id == "old");
        Assert.Contains(report.Recommendations, r => r.Id == "missing");
    }

    [Fact]
    public void Analyze_StrengthBreakdown_SumsToLoginCount()
    {
        var entries = new[]
        {
            Entry("VW", "abc"),
            Entry("W", "password123"),
            Entry("F", "CorrectHorse99"),
            Entry("S", "C0rrect-H0rse!2026"),
            Entry("VS", "C0rrect-H0rse!Battery#Staple-2026-XYZ")
        };

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.Equal(5, report.TotalEntries);
        Assert.Equal(5, report.VeryWeakCount + report.WeakStrengthCount + report.FairCount + report.StrongCount + report.VeryStrongCount);
    }

    [Fact]
    public void Analyze_PoorVault_ScoresLow()
    {
        var shared = "123456";
        var entries = Enumerable.Range(0, 8)
            .Select(i => Entry($"Site{i}", shared, DateTimeOffset.UtcNow.AddDays(-500)))
            .ToList();

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.True(report.SecurityScore < 40);
        Assert.True(report.ReusedCount >= 8);
        Assert.True(report.OldCount >= 8);
    }
}
