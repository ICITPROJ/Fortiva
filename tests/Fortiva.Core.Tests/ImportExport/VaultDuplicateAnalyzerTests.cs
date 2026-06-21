using Fortiva.Core.ImportExport;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.ImportExport;

public sealed class VaultDuplicateAnalyzerTests
{
    private static VaultEntry Entry(string title, string user, string pass, string url)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Username = user,
            Password = pass,
            Url = url
        };

    [Fact]
    public void FindGroups_DetectsExactAndSimilarDuplicates()
    {
        var entries = new List<VaultEntry>
        {
            Entry("Bank", "alice", "same", "https://bank.example/login"),
            Entry("Bank copy", "alice", "same", "https://bank.example"),
            Entry("Bank alt", "alice", "other", "https://bank.example/signin"),
            Entry("Mail", "bob", "x", "https://mail.example")
        };

        var groups = VaultDuplicateAnalyzer.FindGroups(entries);

        Assert.Single(groups);
        var bank = groups[0];
        Assert.Equal(VaultDuplicateKind.SameSiteUser, bank.Kind);
        Assert.Equal(3, bank.EntryIds.Count);
    }

    [Fact]
    public void FindGroups_DetectsSimilarUrlVariations()
    {
        var entries = new List<VaultEntry>
        {
            Entry("IONOS login", "user@test.com", "same", "https://login.ionos.co.uk/"),
            Entry("IONOS www", "user@test.com", "same", "https://www.ionos.co.uk/"),
            Entry("Other", "bob", "x", "https://mail.example")
        };

        var groups = VaultDuplicateAnalyzer.FindGroups(entries);

        Assert.Single(groups);
        var ionos = groups[0];
        Assert.Equal(VaultDuplicateKind.SimilarSite, ionos.Kind);
        Assert.Equal(2, ionos.EntryIds.Count);
    }

    [Fact]
    public void FindGroups_IgnoresUniqueEntries()
    {
        var entries = new List<VaultEntry>
        {
            Entry("A", "u1", "p1", "https://a.example"),
            Entry("B", "u2", "p2", "https://b.example")
        };

        Assert.Empty(VaultDuplicateAnalyzer.FindGroups(entries));
    }

    [Fact]
    public void FindGroups_DetectsManualEntriesWithSameTitleAndUsername()
    {
        var entries = new List<VaultEntry>
        {
            Entry("Work portal", "alice", "secret1", ""),
            Entry("Work portal", "alice", "secret2", ""),
            Entry("Personal mail", "bob", "x", "https://mail.example")
        };

        var groups = VaultDuplicateAnalyzer.FindGroups(entries);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].EntryIds.Count);
        Assert.Equal(VaultDuplicateKind.SameSiteUser, groups[0].Kind);
    }

    [Fact]
    public void FindGroups_LinksManualTitleHostToUrlEntry()
    {
        var sharedId = Guid.NewGuid();
        var entries = new List<VaultEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Amazon",
                Username = "alice",
                Password = "pw",
                Url = "https://www.amazon.com/signin"
            },
            new()
            {
                Id = sharedId,
                Title = "amazon.com",
                Username = "alice",
                Password = "pw",
                Url = ""
            }
        };

        var groups = VaultDuplicateAnalyzer.FindGroups(entries);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].EntryIds.Count);
        Assert.Contains(sharedId, groups[0].EntryIds);
    }

    [Fact]
    public void FindGroups_IncludesManualEntryAlongsideImportedEntry()
    {
        var manualId = Guid.NewGuid();
        var entries = new List<VaultEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "GitHub",
                Username = "dev",
                Password = "same",
                Url = "https://github.com/login",
                ImportSource = "Microsoft Edge (PC)",
                ImportBatchId = Guid.NewGuid()
            },
            new()
            {
                Id = manualId,
                Title = "GitHub login",
                Username = "dev",
                Password = "same",
                Url = "https://github.com"
            }
        };

        var groups = VaultDuplicateAnalyzer.FindGroups(entries);

        Assert.Single(groups);
        Assert.Equal(VaultDuplicateKind.Exact, groups[0].Kind);
        Assert.Contains(manualId, groups[0].EntryIds);
    }
}
