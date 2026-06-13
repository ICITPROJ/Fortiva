using Fortiva.Core.Crypto;
using Fortiva.Core.ImportExport;
using Fortiva.Core.LocalState;
using Fortiva.Core.Password;
using Fortiva.Core.Policy;
using Fortiva.Core.Services;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

/// <summary>Full workflow integration tests: first-run → unlock → CRUD → snapshot → restore.</summary>
public sealed class VaultIntegrationTests : IDisposable
{
    private const string Password = "IntegrationTest-P@ssw0rd!2026";
    private readonly string _dir;

    public VaultIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fv-int-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void FullWorkflow_CreateUnlockAddEditDelete()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);

        // First unlock
        var ctx = engine.Unlock(Password);
        Assert.Empty(ctx.Payload.Entries);

        // Add entries
        engine.AddEntry(ctx, new VaultEntry { Title = "GitHub", Username = "user@github.com", Password = "gh-pass-abc" });
        engine.AddEntry(ctx, new VaultEntry { Title = "AWS Console", Username = "admin@aws.com", Password = "aws-pass-xyz" });
        Assert.Equal(2, ctx.Payload.Entries.Count);
        ctx.Keys.Dispose();

        // Re-unlock and verify persistence
        ctx = engine.Unlock(Password);
        Assert.Equal(2, ctx.Payload.Entries.Count);

        // Update entry
        var entry = ctx.Payload.Entries[0].Clone();
        entry.Password = "new-gh-pass-!!";
        engine.UpdateEntry(ctx, entry);
        Assert.Equal("new-gh-pass-!!", ctx.Payload.Entries[0].Password);

        // Delete entry
        var toDelete = ctx.Payload.Entries[1].Id;
        engine.DeleteEntry(ctx, toDelete);
        Assert.Single(ctx.Payload.Entries);
        ctx.Keys.Dispose();
    }

    [Fact]
    public void Snapshot_CreatedAfterEachSave()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);

        for (var i = 0; i < 6; i++)
        {
            var ctx = engine.Unlock(Password);
            engine.AddEntry(ctx, new VaultEntry { Title = $"Entry {i}" });
            ctx.Keys.Dispose();
        }

        var snapshots = engine.Snapshots.ListSnapshots();
        Assert.True(snapshots.Count > 0);
        Assert.True(snapshots.Count <= VaultConstants.SnapshotCount);
    }

    [Fact]
    public void ChangeMasterPassword_SessionRemainsUsable()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx = engine.Unlock(Password);
        engine.AddEntry(ctx, new VaultEntry { Title = "Before" });
        engine.ChangeMasterPassword(ctx, "NewPassword-XYZ-9876!");
        engine.AddEntry(ctx, new VaultEntry { Title = "After" });
        Assert.Equal(2, ctx.Payload.Entries.Count);
        ctx.Keys.Dispose();
    }

    [Fact]
    public void VerifyMasterPassword_AcceptsCorrectRejectsWrong()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx = engine.Unlock(Password);
        Assert.True(engine.VerifyMasterPassword(ctx, Password));
        Assert.False(engine.VerifyMasterPassword(ctx, "wrong-password"));
        ctx.Keys.Dispose();
    }

    [Fact]
    public void ChangeMasterPassword_OldPasswordFails()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx = engine.Unlock(Password);
        engine.AddEntry(ctx, new VaultEntry { Title = "Test" });
        engine.ChangeMasterPassword(ctx, "NewPassword-XYZ-9876!");
        ctx.Keys.Dispose();

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => engine.Unlock(Password));

        var ctx2 = engine.Unlock("NewPassword-XYZ-9876!");
        Assert.Single(ctx2.Payload.Entries);
        ctx2.Keys.Dispose();
    }

    [Fact]
    public void PolicyEnforcement_RaisesKdfAndBlocksPortable()
    {
        var policy = FortivaPolicy.StrictEnterprise;
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser, policy);
        engine.CreateVault(Password, SecurityLevel.Paranoia);
        var ctx = engine.Unlock(Password);
        Assert.True(ctx.Header.KdfParameters.MemoryKb >= policy.MinArgon2MemoryKb);
        ctx.Keys.Dispose();

        Assert.False(PolicyEnforcer.CanUsePortableMode(policy));
        Assert.Equal(ExportPolicyMode.NoPlaintext, policy.ExportMode);
    }

    [Fact]
    public void BulkImport_10000Entries_PerformanceUnder30s()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx = engine.Unlock(Password);

        var entries = Enumerable.Range(0, 10_000).Select(i => new VaultEntry
        {
            Title = $"Entry {i}",
            Username = $"user{i}@example.com",
            Password = PasswordGenerator.Generate(20, PasswordGeneratorMode.AlphanumericSymbols),
            Url = $"https://example{i % 100}.com"
        }).ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.BulkImport(ctx, entries);
        sw.Stop();

        ctx.Keys.Dispose();

        Assert.Equal(10_000, entries.Count);
        Assert.True(sw.Elapsed.TotalSeconds < 30, $"Bulk import took {sw.Elapsed.TotalSeconds:F1}s — too slow.");

        // Re-unlock and verify
        var ctx2 = engine.Unlock(Password);
        Assert.Equal(10_000, ctx2.Payload.Entries.Count);
        ctx2.Keys.Dispose();
    }

    [Fact]
    public void VaultSession_Lock_DoesNotReenterHostLockHandler()
    {
        using var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.CreateVault(Password, SecurityLevel.Standard);
        session.Unlock(Password);

        var hostLockCalls = 0;
        session.AutoLockRequested += () =>
        {
            hostLockCalls++;
            session.Lock();
        };

        session.Lock();
        Assert.Equal(0, hostLockCalls);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void VaultSession_Lock_AfterCsvImport_DoesNotThrow()
    {
        using var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.CreateVault(Password, SecurityLevel.Standard);
        session.Unlock(Password);

        var imported = Enumerable.Range(0, 50).Select(i => new ImportedCredential
        {
            Entry = new VaultEntry
            {
                Title = $"Site {i}",
                Username = $"user{i}@example.com",
                Password = $"secret{i}",
                Url = $"https://site{i}.example.com"
            },
            SourceCreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
        }).ToList();

        var preview = ImportMergeService.Analyze([], imported);
        var plan = ImportMergeService.BuildApplyPlan(preview, "Browser export", "BrowserCsv", "passwords.csv");
        session.ApplyImport(plan);

        Assert.Equal(50, session.AllEntries().Count);
        session.Lock();
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Search_FindsByTitleUsernameUrlTag()
    {
        using var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.CreateVault(Password, SecurityLevel.Standard);
        session.Unlock(Password);

        session.AddEntry(new VaultEntry { Title = "GitHub", Username = "dev@company.com", Url = "https://github.com", Tags = ["dev", "code"] });
        session.AddEntry(new VaultEntry { Title = "AWS", Username = "ops@company.com", Url = "https://console.aws.amazon.com" });

        var byTitle = session.Search("github").ToList();
        Assert.Single(byTitle);

        var byTag = session.Search("code").ToList();
        Assert.Single(byTag);

        var byDomain = session.Search("aws.amazon").ToList();
        Assert.Single(byDomain);

        var all = session.Search("").ToList();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void PasswordHealthAnalyzer_DetectsWeakAndReused()
    {
        var entries = new List<VaultEntry>
        {
            new() { Title = "A", Password = "password123" },
            new() { Title = "B", Password = "password123" },  // reused
            new() { Title = "C", Password = "abc" },          // weak
            new() { Title = "D", Password = "C0rrect-H0rse!Battery#Staple-2026", ModifiedAt = DateTimeOffset.UtcNow }
        };

        var report = PasswordHealthAnalyzer.Analyze(entries);
        Assert.True(report.WeakCount >= 2);
        Assert.True(report.ReusedCount >= 2);
    }

    [Fact]
    public void CsvImport_ParsesStandardFormat()
    {
        var csv = "name,username,password,url,notes\n" +
                  "GitHub,user@gh.com,ghp_secret,https://github.com,work\n" +
                  "Gmail,user@gmail.com,gmail123,https://gmail.com,personal";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var entries = CsvImporter.ImportFromCsv(stream);
        Assert.Equal(2, entries.Count);
        Assert.Equal("GitHub", entries[0].Title);
        Assert.Equal("ghp_secret", entries[0].Password);
    }

    [Fact]
    public void CsvImport_RejectsOverLongField_InsteadOfTruncating()
    {
        var hugePassword = new string('x', 70 * 1024);
        var csv = "name,username,password,url,notes\n" +
                  $"Site,user,{hugePassword},https://example.com,note";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var ex = Assert.Throws<InvalidDataException>(() => CsvImporter.ImportFromCsv(stream));
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row 2", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RapidLockUnlock_100Cycles_Stable()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx0 = engine.Unlock(Password);
        engine.AddEntry(ctx0, new VaultEntry { Title = "Sentinel", Password = "sentinel-pw" });
        ctx0.Keys.Dispose();

        for (var i = 0; i < 100; i++)
        {
            var ctx = engine.Unlock(Password);
            Assert.Single(ctx.Payload.Entries);
            ctx.Keys.Dispose();
        }
    }

    [Fact]
    public void IntegrityLog_RecordsAllMutations()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault(Password, SecurityLevel.Standard);
        var ctx = engine.Unlock(Password);

        var entry = new VaultEntry { Title = "Test" };
        engine.AddEntry(ctx, entry);
        var updated = entry.Clone(); updated.Password = "new-pw";
        engine.UpdateEntry(ctx, updated);
        engine.DeleteEntry(ctx, entry.Id);
        ctx.Keys.Dispose();

        var ctx2 = engine.Unlock(Password);
        Assert.True(ctx2.Payload.IntegrityLog.Count >= 3);
        var actions = ctx2.Payload.IntegrityLog.Select(l => l.Action).ToList();
        Assert.Contains("add", actions);
        Assert.Contains("update", actions);
        Assert.Contains("delete", actions);
        ctx2.Keys.Dispose();
    }
}
