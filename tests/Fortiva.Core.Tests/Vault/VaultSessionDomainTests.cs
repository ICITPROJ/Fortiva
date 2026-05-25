using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Crypto;
using Fortiva.Core.LocalState;
using Fortiva.Core.Services;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public class VaultSessionDomainTests : IDisposable
{
    private readonly string _dir;

    public VaultSessionDomainTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fortiva-domain-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ResolveForDomain_MatchesExactHost_NotSubstring()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("domain-test-password!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("domain-test-password!");

        session.AddEntry(new VaultEntry
        {
            Title = "Evil",
            Username = "wrong",
            Password = "wrong",
            Url = "https://notexample.com/login"
        });
        session.AddEntry(new VaultEntry
        {
            Title = "Good",
            Username = "user",
            Password = "secret",
            Url = "https://login.example.com"
        });

        var hit = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "login.example.com",
            Url = "https://login.example.com/signin"
        });
        Assert.True(hit.Found);
        Assert.Equal("user", hit.Username);
        Assert.Equal("secret", hit.Password);

        var parentMiss = session.ResolveForDomain(new CredentialRequest { Domain = "example.com" });
        Assert.False(parentMiss.Found);

        var evilHit = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "notexample.com",
            Url = "https://notexample.com/login"
        });
        Assert.True(evilHit.Found);
        Assert.Equal("wrong", evilHit.Username);
    }

    [Fact]
    public void ResolveForDomain_RequiresExactHostMatch()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("subdomain-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("subdomain-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "App",
            Username = "alice",
            Password = "pw",
            Url = "https://app.example.com/signin"
        });

        var exact = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "app.example.com",
            Url = "https://app.example.com/signin"
        });
        Assert.True(exact.Found);
        Assert.Equal("alice", exact.Username);

        var parent = session.ResolveForDomain(new CredentialRequest { Domain = "example.com" });
        Assert.False(parent.Found);
    }

    [Fact]
    public void ResolveForDomain_ReturnsTitle_AndMultipleMatches()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("multi-match-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("multi-match-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "Work GitHub",
            Username = "work@corp.com",
            Password = "pw1",
            Url = "https://github.com/login"
        });
        session.AddEntry(new VaultEntry
        {
            Title = "Personal GitHub",
            Username = "me@home.com",
            Password = "pw2",
            Url = "https://github.com/login"
        });

        var multi = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "github.com",
            Url = "https://github.com/login"
        });
        Assert.False(multi.Found);
        Assert.Equal("multiple_matches", multi.Error);
        Assert.Equal(2, multi.Matches!.Count);

        var personal = multi.Matches!.First(m => m.Title == "Personal GitHub");
        var picked = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "github.com",
            Url = "https://github.com/login",
            EntryId = personal.Id
        });
        Assert.True(picked.Found);
        Assert.Equal("Personal GitHub", picked.Title);
        Assert.Equal("me@home.com", picked.Username);
    }
}
