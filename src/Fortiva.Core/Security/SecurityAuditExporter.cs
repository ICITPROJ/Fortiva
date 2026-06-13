using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fortiva.Core.Updates;

namespace Fortiva.Core.Security;

public sealed class SecurityAuditExportOptions
{
    public string Edition { get; init; } = "Personal";
    public string VaultLocation { get; init; } = "";
    public string AppVersion { get; init; } = Updates.AppVersion.Current;
}

public static class SecurityAuditExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(SecurityAuditReport report, SecurityAuditExportOptions? options = null)
    {
        options ??= new SecurityAuditExportOptions();
        var ph = report.PasswordHealth;
        var payload = new
        {
            schema = "fortiva.security-audit.v1",
            generatedAt = report.RunAt,
            edition = options.Edition,
            appVersion = options.AppVersion,
            vaultLocation = string.IsNullOrWhiteSpace(options.VaultLocation) ? null : options.VaultLocation,
            summary = new
            {
                overallScore = report.OverallScore,
                passCount = report.PassCount,
                infoCount = report.InfoCount,
                warningCount = report.WarningCount,
                criticalCount = report.CriticalCount,
                passwordIssues = report.PasswordFindings,
                settingsIssues = report.SettingsFindings,
                vaultIssues = report.VaultFindings,
                activityIssues = report.ActivityFindings
            },
            passwordHealth = new
            {
                totalLogins = ph.TotalEntries,
                missingPasswords = ph.MissingCount,
                secureCount = ph.SecureCount,
                weakCount = ph.WeakCount,
                reusedCount = ph.ReusedCount,
                oldCount = ph.OldCount,
                securityScore = ph.SecurityScore,
                veryWeakCount = ph.VeryWeakCount,
                weakStrengthCount = ph.WeakStrengthCount,
                fairCount = ph.FairCount,
                strongCount = ph.StrongCount,
                veryStrongCount = ph.VeryStrongCount
            },
            findings = report.Findings.Select(f => new
            {
                category = f.Category,
                id = f.Id,
                title = f.Title,
                detail = f.Detail,
                severity = f.Severity.ToString(),
                priority = f.Priority,
                affectedCount = f.AffectedCount,
                actionHint = f.ActionHint
            })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string ToHtml(SecurityAuditReport report, SecurityAuditExportOptions? options = null)
    {
        options ??= new SecurityAuditExportOptions();
        var ph = report.PasswordHealth;
        var sb = new StringBuilder();
        var runLocal = report.RunAt.LocalDateTime;

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>Fortiva Security Audit Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#1a1a1a;max-width:920px;}");
        sb.AppendLine("h1{margin:0 0 4px;font-size:28px;} .meta{color:#666;font-size:13px;margin-bottom:24px;}");
        sb.AppendLine(".score{font-size:48px;font-weight:700;color:#0a8;} .grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:20px 0;}");
        sb.AppendLine(".card{background:#f5f5f5;border-radius:10px;padding:14px;} .card b{display:block;font-size:22px;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:16px;font-size:13px;}");
        sb.AppendLine("th,td{border:1px solid #ddd;padding:8px;text-align:left;vertical-align:top;}");
        sb.AppendLine("th{background:#eee;} .crit{color:#c22;font-weight:600;} .warn{color:#b70;} .info{color:#06c;} .pass{color:#080;}");
        sb.AppendLine("@media print{body{margin:16px;}}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>Fortiva Security Audit Report</h1>");
        sb.AppendLine($"<div class=\"meta\">Edition: {Esc(options.Edition)} · Version: {Esc(options.AppVersion)} · ");
        sb.AppendLine($"Generated: {runLocal:yyyy-MM-dd HH:mm:ss} local");
        if (!string.IsNullOrWhiteSpace(options.VaultLocation))
            sb.AppendLine($" · Vault: {Esc(options.VaultLocation)}");
        sb.AppendLine("</div>");

        sb.AppendLine($"<div class=\"score\">{report.OverallScore}<span style=\"font-size:18px;color:#666\"> / 100</span></div>");

        sb.AppendLine("<div class=\"grid\">");
        AppendCard(sb, "Passed", report.PassCount.ToString());
        AppendCard(sb, "Warnings", report.WarningCount.ToString());
        AppendCard(sb, "Critical", report.CriticalCount.ToString());
        AppendCard(sb, "Strong logins", ph.SecureCount.ToString());
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Password summary</h2><table>");
        sb.AppendLine("<tr><th>Metric</th><th>Count</th></tr>");
        AppendRow(sb, "Total logins", ph.TotalEntries + ph.MissingCount);
        AppendRow(sb, "Weak", ph.WeakCount);
        AppendRow(sb, "Reused", ph.ReusedCount);
        AppendRow(sb, "Old (12+ months)", ph.OldCount);
        AppendRow(sb, "Missing passwords", ph.MissingCount);
        AppendRow(sb, "Very strong", ph.VeryStrongCount);
        AppendRow(sb, "Strong", ph.StrongCount);
        AppendRow(sb, "Fair", ph.FairCount);
        AppendRow(sb, "Weak strength bucket", ph.WeakStrengthCount);
        AppendRow(sb, "Very weak", ph.VeryWeakCount);
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Findings</h2><table>");
        sb.AppendLine("<tr><th>Severity</th><th>Category</th><th>Title</th><th>Detail</th><th>Affected</th></tr>");
        foreach (var f in report.Findings.OrderBy(x => x.Priority).ThenByDescending(x => x.Severity))
        {
            var cls = f.Severity switch
            {
                AuditSeverity.Critical => "crit",
                AuditSeverity.Warning => "warn",
                AuditSeverity.Pass => "pass",
                _ => "info"
            };
            sb.AppendLine(
                $"<tr><td class=\"{cls}\">{Esc(f.Severity.ToString())}</td>" +
                $"<td>{Esc(f.Category)}</td><td>{Esc(f.Title)}</td>" +
                $"<td>{Esc(f.Detail)}</td><td>{f.AffectedCount}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<p class=\"meta\">No passwords or master key material are included in this report. ");
        sb.AppendLine("Open this file in a browser and use <strong>Print → Save as PDF</strong> to create a PDF copy.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendCard(StringBuilder sb, string label, string value)
        => sb.AppendLine($"<div class=\"card\"><b>{Esc(value)}</b>{Esc(label)}</div>");

    private static void AppendRow(StringBuilder sb, string label, int value)
        => sb.AppendLine($"<tr><td>{Esc(label)}</td><td>{value}</td></tr>");

    private static string Esc(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
