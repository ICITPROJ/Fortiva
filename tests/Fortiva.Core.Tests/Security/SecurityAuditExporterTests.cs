using Fortiva.Core.Security;

namespace Fortiva.Core.Tests.Security;

public class SecurityAuditExporterTests
{
    [Fact]
    public void ToJson_ContainsSchemaAndSummary()
    {
        var report = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = []
        });

        var json = SecurityAuditExporter.ToJson(report, new SecurityAuditExportOptions
        {
            Edition = "Personal",
            AppVersion = "1.0.0-test"
        });

        Assert.Contains("fortiva.security-audit.v1", json);
        Assert.Contains("\"edition\": \"Personal\"", json);
        Assert.Contains("\"overallScore\"", json);
        Assert.Contains("\"findings\"", json);
    }

    [Fact]
    public void ToHtml_ContainsScoreAndFindings_NoSecrets()
    {
        var report = SecurityAuditRunner.Run(new SecurityAuditContext
        {
            Entries = [],
            WindowsHelloConfigured = false
        });

        var html = SecurityAuditExporter.ToHtml(report);

        Assert.Contains("Fortiva Security Audit Report", html);
        Assert.Contains(report.OverallScore.ToString(), html);
        Assert.Contains("Save as PDF", html);
        Assert.DoesNotContain("password123", html);
    }
}
