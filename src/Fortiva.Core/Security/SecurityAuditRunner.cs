using Fortiva.Core.Audit;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Security;

public enum AuditSeverity
{
    Pass = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

public sealed class SecurityAuditFinding
{
    public string Category { get; init; } = "";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public AuditSeverity Severity { get; init; }
    public int Priority { get; init; }
    public int AffectedCount { get; init; }
    public string? ActionHint { get; init; }
}

public sealed class SecurityAuditContext
{
    public required IEnumerable<VaultEntry> Entries { get; init; }
    public int AutoLockSeconds { get; init; } = 300;
    public int ClipboardClearSeconds { get; init; } = 30;
    public bool WindowsHelloConfigured { get; init; }
    public bool ParanoiaMode { get; init; }
    public int SnapshotCount { get; init; }
    public IReadOnlyList<AuditEvent>? AuditEvents { get; init; }
    public bool IncludeActivityAudit { get; init; }
    public IReadOnlyList<ImportBatch>? ImportBatches { get; init; }
}

public sealed class SecurityAuditReport
{
    public DateTimeOffset RunAt { get; init; } = DateTimeOffset.UtcNow;
    public Password.PasswordHealthReport PasswordHealth { get; init; } = new();
    public int OverallScore { get; init; }
    public int PassCount { get; init; }
    public int InfoCount { get; init; }
    public int WarningCount { get; init; }
    public int CriticalCount { get; init; }
    public int PasswordFindings { get; init; }
    public int SettingsFindings { get; init; }
    public int VaultFindings { get; init; }
    public int ActivityFindings { get; init; }
    public List<SecurityAuditFinding> Findings { get; init; } = [];
}

public static class SecurityAuditRunner
{
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromDays(30);

    public static SecurityAuditReport Run(SecurityAuditContext context)
    {
        var entries = context.Entries.Where(e => !e.IsSecureNote).ToList();
        var passwordHealth = Password.PasswordHealthAnalyzer.Analyze(context.Entries);
        var findings = new List<SecurityAuditFinding>();

        AddPasswordFindings(findings, passwordHealth);
        AddSettingsFindings(findings, context, passwordHealth);
        AddVaultFindings(findings, entries, context);
        AddImportFindings(findings, context);
        if (context.IncludeActivityAudit && context.AuditEvents is { Count: > 0 })
            AddActivityFindings(findings, context.AuditEvents);
        else if (context.IncludeActivityAudit)
            AddActivityFindings(findings, []);

        AddPositiveFindings(findings, context, passwordHealth, entries);

        findings = findings.OrderBy(f => f.Priority).ThenByDescending(f => f.Severity).ToList();

        var overall = ComputeOverallScore(passwordHealth.SecurityScore, findings);

        return new SecurityAuditReport
        {
            RunAt = DateTimeOffset.UtcNow,
            PasswordHealth = passwordHealth,
            OverallScore = overall,
            PassCount = findings.Count(f => f.Severity == AuditSeverity.Pass),
            InfoCount = findings.Count(f => f.Severity == AuditSeverity.Info),
            WarningCount = findings.Count(f => f.Severity == AuditSeverity.Warning),
            CriticalCount = findings.Count(f => f.Severity == AuditSeverity.Critical),
            PasswordFindings = findings.Count(f => f.Category == "Passwords" && f.Severity >= AuditSeverity.Warning),
            SettingsFindings = findings.Count(f => f.Category == "Settings" && f.Severity >= AuditSeverity.Warning),
            VaultFindings = findings.Count(f => f.Category == "Vault" && f.Severity >= AuditSeverity.Warning),
            ActivityFindings = findings.Count(f => f.Category == "Activity" && f.Severity >= AuditSeverity.Warning),
            Findings = findings
        };
    }

    internal static int ComputeOverallScore(int passwordScore, IReadOnlyList<SecurityAuditFinding> findings)
    {
        var penalty = 0;
        foreach (var f in findings)
        {
            penalty += f.Severity switch
            {
                AuditSeverity.Critical => 12 + Math.Min(f.AffectedCount, 5) * 2,
                AuditSeverity.Warning => 5 + Math.Min(f.AffectedCount, 3),
                AuditSeverity.Info => 1,
                _ => 0
            };
        }

        return Math.Max(0, Math.Min(100, (passwordScore * 7 + 300) / 10 - Math.Min(35, penalty)));
    }

    private static void AddPasswordFindings(List<SecurityAuditFinding> findings, Password.PasswordHealthReport health)
    {
        if (health.ReusedCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Passwords",
                Id = "reused",
                Title = $"{health.ReusedCount} account{(health.ReusedCount == 1 ? "" : "s")} share passwords",
                Detail = "One breach can unlock multiple sites. Every login should have its own unique password.",
                Severity = health.ReusedCount >= 3 ? AuditSeverity.Critical : AuditSeverity.Warning,
                Priority = 1,
                AffectedCount = health.ReusedCount,
                ActionHint = "generator"
            });
        }

        if (health.WeakCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Passwords",
                Id = "weak",
                Title = $"{health.WeakCount} weak password{(health.WeakCount == 1 ? "" : "s")} detected",
                Detail = "Short, common, or low-entropy passwords are vulnerable to guessing and breach lists.",
                Severity = health.WeakCount >= 5 ? AuditSeverity.Critical : AuditSeverity.Warning,
                Priority = 2,
                AffectedCount = health.WeakCount,
                ActionHint = "generator"
            });
        }

        if (health.OldCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Passwords",
                Id = "old",
                Title = $"{health.OldCount} password{(health.OldCount == 1 ? "" : "s")} not rotated in 12+ months",
                Detail = "Rotate banking, email, and work accounts first, then update the rest over time.",
                Severity = AuditSeverity.Warning,
                Priority = 3,
                AffectedCount = health.OldCount,
                ActionHint = "vault"
            });
        }

        if (health.MissingCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Passwords",
                Id = "missing",
                Title = $"{health.MissingCount} entr{(health.MissingCount == 1 ? "y is" : "ies are")} missing passwords",
                Detail = "Incomplete entries cannot autofill or be evaluated for strength.",
                Severity = AuditSeverity.Warning,
                Priority = 4,
                AffectedCount = health.MissingCount,
                ActionHint = "vault"
            });
        }
    }

    private static void AddSettingsFindings(
        List<SecurityAuditFinding> findings,
        SecurityAuditContext context,
        Password.PasswordHealthReport health)
    {
        if (context.AutoLockSeconds > 600)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "autolock-slow",
                Title = "Auto-lock is set above 10 minutes",
                Detail = $"Vault stays unlocked for {context.AutoLockSeconds / 60} minutes of inactivity. Shorter timeouts reduce exposure if you step away.",
                Severity = AuditSeverity.Warning,
                Priority = 10,
                AffectedCount = 1,
                ActionHint = "settings"
            });
        }

        if (context.ClipboardClearSeconds <= 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "clipboard-off",
                Title = "Clipboard auto-clear is disabled",
                Detail = "Copied passwords may remain on the clipboard indefinitely. Enable auto-clear (15–30 seconds recommended).",
                Severity = AuditSeverity.Critical,
                Priority = 5,
                AffectedCount = 1,
                ActionHint = "settings"
            });
        }
        else if (context.ClipboardClearSeconds > 60)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "clipboard-slow",
                Title = "Clipboard clears slowly",
                Detail = $"Passwords stay on the clipboard for {context.ClipboardClearSeconds} seconds. Consider 30 seconds or less.",
                Severity = AuditSeverity.Info,
                Priority = 11,
                AffectedCount = 1,
                ActionHint = "settings"
            });
        }

        if (!context.WindowsHelloConfigured)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "hello-off",
                Title = "Windows Hello is not set up",
                Detail = "Face, fingerprint, or PIN unlock adds fast re-auth without weakening your master password.",
                Severity = AuditSeverity.Info,
                Priority = 12,
                AffectedCount = 1,
                ActionHint = "settings"
            });
        }

        if (!context.ParanoiaMode && (health.WeakCount > 0 || health.ReusedCount > 0))
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "paranoia-off",
                Title = "Paranoia Mode is off while issues remain",
                Detail = "Paranoia Mode tightens clipboard and visibility rules while you work through password fixes.",
                Severity = AuditSeverity.Info,
                Priority = 13,
                AffectedCount = 1,
                ActionHint = "settings"
            });
        }
    }

    private static void AddVaultFindings(
        List<SecurityAuditFinding> findings,
        List<VaultEntry> entries,
        SecurityAuditContext context)
    {
        var httpCount = entries.Count(e =>
            !string.IsNullOrWhiteSpace(e.Url) &&
            e.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        if (httpCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Vault",
                Id = "http-urls",
                Title = $"{httpCount} entr{(httpCount == 1 ? "y uses" : "ies use")} unencrypted HTTP URLs",
                Detail = "Prefer HTTPS sites where possible. Credentials sent over HTTP can be intercepted on untrusted networks.",
                Severity = httpCount >= 3 ? AuditSeverity.Warning : AuditSeverity.Info,
                Priority = 20,
                AffectedCount = httpCount,
                ActionHint = "vault"
            });
        }

        if (context.SnapshotCount == 0 && entries.Count > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Vault",
                Id = "no-snapshots",
                Title = "No vault snapshots yet",
                Detail = "Fortiva keeps rolling encrypted snapshots after saves. Make a change and save to create your first recovery point.",
                Severity = AuditSeverity.Info,
                Priority = 21,
                AffectedCount = 0
            });
        }

        var noUrl = entries.Count(e => string.IsNullOrWhiteSpace(e.Url) && !string.IsNullOrEmpty(e.Password));
        if (noUrl >= 5)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Vault",
                Id = "missing-urls",
                Title = $"{noUrl} logins have no website URL",
                Detail = "Adding URLs improves autofill matching and makes duplicate detection more accurate.",
                Severity = AuditSeverity.Info,
                Priority = 22,
                AffectedCount = noUrl,
                ActionHint = "vault"
            });
        }
    }

    private static void AddImportFindings(List<SecurityAuditFinding> findings, SecurityAuditContext context)
    {
        var batches = context.ImportBatches;
        if (batches is null or { Count: 0 })
            return;

        var duplicateCount = batches.Sum(b => b.SkippedDuplicateCount);
        if (duplicateCount <= 0)
            return;

        var batchCount = batches.Count(b => b.SkippedDuplicateCount > 0);
        findings.Add(new SecurityAuditFinding
        {
            Category = "Vault",
            Id = "import-duplicates",
            Title = $"{duplicateCount} duplicate{(duplicateCount == 1 ? "" : "s")} skipped during import",
            Detail = $"From {batchCount} import{(batchCount == 1 ? "" : "s")}. Existing vault entries were kept — import never deletes or overwrites without your explicit choice.",
            Severity = AuditSeverity.Info,
            Priority = 23,
            AffectedCount = duplicateCount,
            ActionHint = "import-duplicates"
        });
    }

    private static void AddActivityFindings(List<SecurityAuditFinding> findings, IReadOnlyList<AuditEvent> events)
    {
        var cutoff = DateTimeOffset.UtcNow - ActivityWindow;
        var recent = events.Where(e => e.Timestamp >= cutoff).ToList();
        var failedUnlocks = recent.Count(e => e.EventType == AuditEventType.UnlockFailure);
        var policyViolations = recent.Count(e => e.EventType == AuditEventType.PolicyViolation);

        if (failedUnlocks >= 5)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Activity",
                Id = "failed-unlocks",
                Title = $"{failedUnlocks} failed unlock attempts in 30 days",
                Detail = "Repeated failures may indicate credential guessing or users forgetting the master password.",
                Severity = failedUnlocks >= 10 ? AuditSeverity.Critical : AuditSeverity.Warning,
                Priority = 6,
                AffectedCount = failedUnlocks,
                ActionHint = "audit"
            });
        }

        if (policyViolations > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Activity",
                Id = "policy-violations",
                Title = $"{policyViolations} policy violation{(policyViolations == 1 ? "" : "s")} in 30 days",
                Detail = "Review the audit log for blocked exports, clipboard use, or other policy breaches.",
                Severity = AuditSeverity.Critical,
                Priority = 7,
                AffectedCount = policyViolations,
                ActionHint = "audit"
            });
        }

        if (recent.Count == 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Activity",
                Id = "no-events",
                Title = "No audit events recorded yet",
                Detail = "Unlock and configuration events will appear here as users interact with Fortiva.",
                Severity = AuditSeverity.Info,
                Priority = 30,
                AffectedCount = 0,
                ActionHint = "audit"
            });
        }
    }

    private static void AddPositiveFindings(
        List<SecurityAuditFinding> findings,
        SecurityAuditContext context,
        Password.PasswordHealthReport health,
        List<VaultEntry> entries)
    {
        if (health.SecurityScore >= 85 && health.ReusedCount == 0 && health.WeakCount == 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Passwords",
                Id = "passwords-strong",
                Title = "Password hygiene is excellent",
                Detail = "No weak or reused passwords were found in your vault.",
                Severity = AuditSeverity.Pass,
                Priority = 90,
                AffectedCount = health.SecureCount
            });
        }

        if (context.WindowsHelloConfigured)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "hello-on",
                Title = "Windows Hello is configured",
                Detail = "Quick unlock is enabled without storing your master password.",
                Severity = AuditSeverity.Pass,
                Priority = 91,
                AffectedCount = 1
            });
        }

        if (context.AutoLockSeconds is > 0 and <= 300)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "autolock-good",
                Title = "Auto-lock timeout is sensible",
                Detail = $"Vault locks after {context.AutoLockSeconds} seconds of inactivity.",
                Severity = AuditSeverity.Pass,
                Priority = 92,
                AffectedCount = 1
            });
        }

        if (context.ClipboardClearSeconds is > 0 and <= 45)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Settings",
                Id = "clipboard-good",
                Title = "Clipboard auto-clear is enabled",
                Detail = $"Copied passwords clear after {context.ClipboardClearSeconds} seconds.",
                Severity = AuditSeverity.Pass,
                Priority = 93,
                AffectedCount = 1
            });
        }

        if (context.SnapshotCount > 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Vault",
                Id = "snapshots-on",
                Title = $"{context.SnapshotCount} encrypted snapshot{(context.SnapshotCount == 1 ? "" : "s")} available",
                Detail = "Rolling snapshots protect against corruption and accidental overwrites.",
                Severity = AuditSeverity.Pass,
                Priority = 94,
                AffectedCount = context.SnapshotCount
            });
        }

        if (entries.Count == 0)
        {
            findings.Add(new SecurityAuditFinding
            {
                Category = "Vault",
                Id = "empty-vault",
                Title = "Vault is empty - add logins to begin",
                Detail = "Import from Chrome/Edge CSV or add entries manually, then run this audit again.",
                Severity = AuditSeverity.Info,
                Priority = 95,
                AffectedCount = 0,
                ActionHint = "import"
            });
        }

        findings.Add(new SecurityAuditFinding
        {
            Category = "Vault",
            Id = "backup-tip",
            Title = "Export an encrypted backup periodically",
            Detail = "Save a .fvab file to cloud storage so you can restore on a new PC with your master password.",
            Severity = AuditSeverity.Info,
            Priority = 96,
            AffectedCount = 0,
            ActionHint = "export"
        });
    }
}
