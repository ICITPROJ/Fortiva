using Fortiva.Core.ImportExport;
using Fortiva.Core.LocalState;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.ImportExport;

public class VaultExporterTests : IDisposable
{
    private readonly string _dir;

    public VaultExporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-export-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ExportPlaintextCsv_BlockedWhenPolicyDisallows()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("export-policy-test!", SecurityLevel.Standard);
        var ctx = engine.Unlock("export-policy-test!");
        try
        {
            engine.AddEntry(ctx, new VaultEntry { Title = "Site", Username = "u", Password = "p" });
            var policy = new FortivaPolicy { ExportMode = ExportPolicyMode.NoPlaintext };

            var ex = Assert.Throws<InvalidOperationException>(() => VaultExporter.ExportPlaintextCsv(ctx, policy));
            Assert.Contains("disabled by policy", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.Keys.Dispose();
        }
    }

    [Fact]
    public void ExportPlaintextCsv_AllowedWhenPolicyPermits()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("export-allow-test!", SecurityLevel.Standard);
        var ctx = engine.Unlock("export-allow-test!");
        try
        {
            engine.AddEntry(ctx, new VaultEntry { Title = "Site", Username = "u", Password = "p" });
            var policy = new FortivaPolicy { ExportMode = ExportPolicyMode.PlaintextWithWarning };

            var csv = VaultExporter.ExportPlaintextCsv(ctx, policy);
            Assert.Contains("Site", csv);
            Assert.Contains("u", csv);
        }
        finally
        {
            ctx.Keys.Dispose();
        }
    }
}
