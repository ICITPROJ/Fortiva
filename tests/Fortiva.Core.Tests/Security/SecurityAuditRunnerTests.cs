using Fortiva.Core.Audit;
using Fortiva.Core.Security;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Security;

public class SecurityAuditRunnerTests
{
    private static VaultEntry Entry(string title, string password, string url = "", DateTimeOffset? modified = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Username = "user",
            Password = password,
            Url = url,
            ModifiedAt = modified ?? DateTimeOffset.UtcNow
        };

    [Fact]
    public void Run_EmptyVault_IncludesStarterGuidance()
    {
        var report = SecurityAuditRunner.Run(new SecurityAuditContext { Entries = [] });
        Assert.Contains(report.Findings, f => f.Id == "empty-vault");
        Assert.True(report.OverallScore >= 90);
    }

    [Fact]
    public void Run_DetectsClipboardAndHttpIssues()
    {
        var report = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = [Entry("Legacy", "C0rrect-H0rse!2026-XYZ", "http://example.com")],
            ClipboardClearSeconds = 0,
            AutoLockSeconds = 900
        });

        Assert.Contains(report.Findings, f => f.Id == "clipboard-off" && f.Severity == AuditSeverity.Critical);
        Assert.Contains(report.Findings, f => f.Id == "http-urls");
        Assert.Contains(report.Findings, f => f.Id == "autolock-slow");
    }

    [Fact]
    public void Run_StrongVault_IncludesPassFindings()
    {
        var report = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries =
            [
                Entry("Bank", "C0rrect-H0rse!Battery#1-2026", "https://bank.example"),
                Entry("Mail", "C0rrect-H0rse!Battery#2-2026", "https://mail.example")
            ],
            WindowsHelloConfigured = true,
            AutoLockSeconds = 120,
            ClipboardClearSeconds = 20,
            SnapshotCount = 2
        });

        Assert.Contains(report.Findings, f => f.Severity == AuditSeverity.Pass && f.Id == "hello-on");
        Assert.Contains(report.Findings, f => f.Severity == AuditSeverity.Pass && f.Id == "snapshots-on");
        Assert.True(report.OverallScore >= 75);
    }

    [Fact]
    public void Run_EnterpriseActivity_FlagsFailedUnlocks()
    {
        var events = Enumerable.Range(0, 6).Select(_ => new AuditEvent
        {
            EventType = AuditEventType.UnlockFailure,
            Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
            Success = false,
            Message = "bad password"
        }).ToList();

        var report = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = [Entry("A", "C0rrect-H0rse!2026-XYZ")],
            IncludeActivityAudit = true,
            AuditEvents = events
        });

        Assert.Contains(report.Findings, f => f.Id == "failed-unlocks");
        Assert.True(report.ActivityFindings >= 1);
    }

    [Fact]
    public void Run_CriticalFindings_LowersOverallScore()
    {
        var clean = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = [Entry("A", "C0rrect-H0rse!Battery#1-2026", "https://a.example")]
        });
        var risky = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries =
            [
                Entry("A", "123", "http://a.example"),
                Entry("B", "123", "http://b.example")
            ],
            ClipboardClearSeconds = 0
        });

        Assert.True(risky.OverallScore < clean.OverallScore);
        Assert.True(risky.CriticalCount >= 1);
    }
}
