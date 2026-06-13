using Fortiva.Core.ImportExport;
using Fortiva.Core.LocalState;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.ImportExport;

public sealed class ImportMergeServiceTests : IDisposable
{
    private readonly string _dir;

    public ImportMergeServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-import-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Analyze_ClassifiesNewDuplicateAndConflict()
    {
        var existing = new List<VaultEntry>
        {
            new() { Title = "Bank", Username = "alice", Password = "same", Url = "https://bank.example/login" },
            new() { Title = "Shop", Username = "bob", Password = "old-pass", Url = "https://shop.example" }
        };

        var incoming = new List<ImportedCredential>
        {
            new() { Entry = new VaultEntry { Title = "Bank", Username = "alice", Password = "same", Url = "https://bank.example" } },
            new() { Entry = new VaultEntry { Title = "Shop", Username = "bob", Password = "new-pass", Url = "https://shop.example" } },
            new() { Entry = new VaultEntry { Title = "Mail", Username = "carol", Password = "x", Url = "https://mail.example" } }
        };

        var preview = ImportMergeService.Analyze(existing, incoming);

        Assert.Equal(1, preview.NewCount);
        Assert.Equal(1, preview.DuplicateCount);
        Assert.Equal(1, preview.ConflictCount);
    }

    [Fact]
    public void Analyze_TreatsDuplicateRowsInSameFileAsDuplicate()
    {
        var incoming = new List<ImportedCredential>
        {
            new() { Entry = new VaultEntry { Title = "Site", Username = "user", Password = "a", Url = "https://site.example" } },
            new() { Entry = new VaultEntry { Title = "Site", Username = "user", Password = "b", Url = "https://site.example" } }
        };

        var preview = ImportMergeService.Analyze([], incoming);

        Assert.Equal(1, preview.NewCount);
        Assert.Equal(1, preview.DuplicateCount);
    }

    [Fact]
    public void BuildApplyPlan_NeverUpdatesWithoutUseImportedChoice()
    {
        var existing = new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = "Site",
            Username = "user",
            Password = "keep-me",
            Url = "https://site.example"
        };

        var incoming = new ImportedCredential
        {
            Entry = new VaultEntry { Title = "Site", Username = "user", Password = "other", Url = "https://site.example" }
        };

        var preview = ImportMergeService.Analyze([existing], [incoming]);
        preview.Items[0].Resolution = ImportConflictChoice.KeepExisting;

        var plan = ImportMergeService.BuildApplyPlan(preview, "Edge export", "BrowserCsv", "edge.csv");

        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToUpdate);
        Assert.Equal(1, plan.ConflictKeptExistingCount);
    }

    [Fact]
    public void BuildApplyPlan_StoresUserMetadataOnBatchAndEntries()
    {
        var preview = ImportMergeService.Analyze([], new List<ImportedCredential>
        {
            new()
            {
                Entry = new VaultEntry { Title = "Site", Username = "u", Password = "p", Url = "https://site.example" }
            }
        });

        var metadata = ImportBatchMetadata.Create(
            "Old laptop Chrome",
            "Home PC · Edge profile",
            "Recovered after reinstalling Windows.",
            "fallback.csv (CSV import)");

        var plan = ImportMergeService.BuildApplyPlan(
            preview,
            "CSV import",
            "BrowserCsv",
            "passwords.csv",
            metadata);

        Assert.Equal("Old laptop Chrome", plan.Batch.DisplayName);
        Assert.Equal("Home PC · Edge profile", plan.Batch.SourceHint);
        Assert.Equal("Recovered after reinstalling Windows.", plan.Batch.Notes);
        Assert.Equal("Old laptop Chrome", plan.Batch.ProvenanceLabel);
        Assert.Equal("Old laptop Chrome", plan.ToAdd[0].ImportSource);
        Assert.Contains(plan.ToAdd[0].Tags, t => t.Contains("Old laptop Chrome", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyImport_RecordsBatchAndProvenance()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("import-prov-test!", SecurityLevel.Standard);
        var ctx = engine.Unlock("import-prov-test!");
        try
        {
            var preview = ImportMergeService.Analyze([], new List<ImportedCredential>
            {
                new()
                {
                    Entry = new VaultEntry { Title = "A", Username = "u", Password = "p", Url = "https://a.example" },
                    SourceCreatedAt = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero)
                }
            });
            var plan = ImportMergeService.BuildApplyPlan(preview, "Browser export", "BrowserCsv", "b.csv");
            engine.ApplyImport(ctx, plan);

            Assert.Single(ctx.Payload.Entries);
            Assert.Single(ctx.Payload.ImportBatches);
            var entry = ctx.Payload.Entries[0];
            Assert.Equal("Browser export", entry.ImportSource);
            Assert.Equal("Browser export", plan.Batch.ProvenanceLabel);
            Assert.Equal(plan.Batch.Id, entry.ImportBatchId);
            Assert.Equal(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), entry.SourceCreatedAt);
            Assert.Contains(entry.Tags, t => t.StartsWith("import:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ctx.Keys.Dispose();
        }
    }

    [Fact]
    public void NormalizeImportedUrl_InfersFromTitleWhenMissing()
    {
        var entry = new VaultEntry { Title = "login.ionos.co.uk", Username = "u", Password = "p" };
        ImportMergeService.NormalizeImportedUrl(entry);
        Assert.Equal("https://login.ionos.co.uk", entry.Url);
    }

    [Fact]
    public void ParseOptionalDate_AcceptsIsoAndUnix()
    {
        var iso = CsvImporter.ParseOptionalDate("2024-06-01T12:30:00Z");
        Assert.NotNull(iso);
        Assert.Equal(2024, iso!.Value.Year);

        var unix = CsvImporter.ParseOptionalDate("1717242600");
        Assert.NotNull(unix);
    }
}
