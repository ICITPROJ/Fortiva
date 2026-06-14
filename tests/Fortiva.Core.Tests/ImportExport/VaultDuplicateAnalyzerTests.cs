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
    public void FindGroups_IgnoresUniqueEntries()
    {
        var entries = new List<VaultEntry>
        {
            Entry("A", "u1", "p1", "https://a.example"),
            Entry("B", "u2", "p2", "https://b.example")
        };

        Assert.Empty(VaultDuplicateAnalyzer.FindGroups(entries));
    }
}
