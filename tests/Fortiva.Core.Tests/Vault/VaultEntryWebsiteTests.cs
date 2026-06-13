using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

/// <summary>Fast unit tests for <see cref="VaultEntryWebsite"/> (no vault I/O).</summary>
public class VaultEntryWebsiteTests
{
    [Fact]
    public void GetEffectiveUrl_FindsHostEmbeddedInTitle()
    {
        var entry = new VaultEntry
        {
            Title = "IONOS login.ionos.co.uk",
            Username = "u",
            Password = "p"
        };

        Assert.Equal("https://login.ionos.co.uk", VaultEntryWebsite.GetEffectiveUrl(entry));
    }

    [Fact]
    public void GetEffectiveUrl_FindsUrlInNotes()
    {
        var entry = new VaultEntry
        {
            Title = "IONOS",
            Notes = "Site: https://login.ionos.co.uk/idp/",
            Username = "u",
            Password = "p"
        };

        Assert.Equal("https://login.ionos.co.uk/idp", VaultEntryWebsite.GetEffectiveUrl(entry));
    }

    [Fact]
    public void NormalizeWebsite_PersistsUrlFromTitle()
    {
        var entry = new VaultEntry
        {
            Title = "login.ionos.co.uk",
            Username = "u",
            Password = "p"
        };

        VaultEntryWebsite.NormalizeWebsite(entry);
        Assert.Equal("https://login.ionos.co.uk", entry.Url);
    }
}
