using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Audit;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Audit;

public sealed class PersonalAuditLoggerTests : IDisposable
{
    private readonly string _dir;

    public PersonalAuditLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FortivaPersonalAudit-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ForPersonal_WritesToPersonalAuditDirectory()
    {
        var logger = new AuditLogger(_dir);
        logger.Log(AuditEventType.UnlockSuccess, "Test unlock");

        var events = logger.ReadRecent(10);
        Assert.Single(events);
        Assert.Equal(AuditEventType.UnlockSuccess, events[0].EventType);
    }

    [Fact]
    public void PersonalAuditDirectory_IsUnderFortivaPersonalLocalAppData()
    {
        Assert.StartsWith(FortivaPaths.PersonalCrashLogDirectory, FortivaPaths.PersonalAuditDirectory);
        Assert.EndsWith("audit", FortivaPaths.PersonalAuditDirectory);
    }

    [Fact]
    public void LoadOrCreateHmacKey_MigratesLegacyLocalMachineKeyToCurrentUser()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FortivaPersonal", "audit-migrate-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var entropy = Encoding.UTF8.GetBytes("Fortiva.Audit.HMAC.v1");
            var key = RandomNumberGenerator.GetBytes(32);
            var protectedKey = ProtectedData.Protect(key, entropy, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(Path.Combine(dir, ".audit.hmac.key"), protectedKey);

            var logger = new AuditLogger(dir);
            logger.Log(AuditEventType.Lock, "migrate test");

            var events = logger.ReadRecent(5);
            Assert.Single(events);
            Assert.Equal(AuditEventType.Lock, events[0].EventType);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
