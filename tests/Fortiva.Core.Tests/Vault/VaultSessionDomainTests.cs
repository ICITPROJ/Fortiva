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

    private static CredentialResponse ResolveWithNonce(VaultSession session, CredentialRequest req)
    {
        var listed = session.ListMatchesForDomain(req);
        req.FillNonce = listed.FillNonce;
        return session.ResolveForDomain(req);
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

        var hit = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "login.example.com",
            Url = "https://login.example.com/signin"
        });
        Assert.True(hit.Found);
        Assert.Equal("user", hit.Username);
        Assert.Equal("secret", hit.Password);

        // Listing uses registrable-domain rules; password release requires exact host.
        var parentListed = session.ListMatchesForDomain(new CredentialRequest { Domain = "example.com" });
        Assert.True(parentListed.Found);
        Assert.Single(parentListed.Matches!);

        var parentRelease = ResolveWithNonce(session, new CredentialRequest { Domain = "example.com" });
        Assert.False(parentRelease.Found);
        Assert.Equal("no_match", parentRelease.Error);

        var evilHit = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "notexample.com",
            Url = "https://notexample.com/login"
        });
        Assert.True(evilHit.Found);
        Assert.Equal("wrong", evilHit.Username);
    }

    [Fact]
    public void ResolveForDomain_MatchesSameRegistrableDomain()
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

        var exact = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "app.example.com",
            Url = "https://app.example.com/signin"
        });
        Assert.True(exact.Found);
        Assert.Equal("alice", exact.Username);

        var parentListed = session.ListMatchesForDomain(new CredentialRequest { Domain = "example.com" });
        Assert.True(parentListed.Found);
        Assert.Single(parentListed.Matches!);

        var parentRelease = ResolveWithNonce(session, new CredentialRequest { Domain = "example.com" });
        Assert.False(parentRelease.Found);
        Assert.Equal("no_match", parentRelease.Error);
    }

    [Fact]
    public void ListMatchesForDomain_MatchesIonos_FromTitleWhenUrlMissing()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("ionos-title!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("ionos-title!");

        session.AddEntry(new VaultEntry
        {
            Title = "www.ionos.co.uk",
            Username = "edge-import@example.com",
            Password = "pw"
        });

        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });

        Assert.NotNull(listed.Matches);
        Assert.Single(listed.Matches!);
        Assert.Equal("edge-import@example.com", listed.Matches![0].Username);
    }

    [Fact]
    public void ListMatchesForDomain_MatchesIonos_FromEmbeddedTitleHost()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("embedded-title!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("embedded-title!");

        session.AddEntry(new VaultEntry
        {
            Title = "IONOS account login.ionos.co.uk",
            Username = "user@example.com",
            Password = "pw"
        });

        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });

        Assert.NotNull(listed.Matches);
        Assert.Single(listed.Matches!);
        Assert.Equal("user@example.com", listed.Matches![0].Username);
    }

    [Fact]
    public void ResolveForDomain_MatchesIonosSubdomains()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("ionos-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("ionos-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "IONOS",
            Username = "user@example.com",
            Password = "pw",
            Url = "https://login.ionos.co.uk/"
        });

        var loginPage = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });
        Assert.True(loginPage.Found);
        Assert.Equal("user@example.com", loginPage.Username);
    }

    [Fact]
    public void ResolveForDomain_RejectsCrossSubdomainCredentialRelease()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("cross-sub!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("cross-sub!");

        session.AddEntry(new VaultEntry
        {
            Title = "IONOS marketing",
            Username = "user@example.com",
            Password = "pw",
            Url = "https://www.ionos.co.uk/"
        });

        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });
        Assert.Single(listed.Matches!);
        Assert.False(listed.Matches![0].Releasable);

        var denied = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });
        Assert.False(denied.Found);
        Assert.Equal("no_match", denied.Error);

        var entryId = listed.Matches![0].Id;
        var listedAgain = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/"
        });
        var byId = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "login.ionos.co.uk",
            Url = "https://login.ionos.co.uk/",
            EntryId = entryId,
            FillNonce = listedAgain.FillNonce
        });
        Assert.False(byId.Found);
        Assert.Equal("no_match", byId.Error);
    }

    [Fact]
    public void ResolveForDomain_RejectsSecureNote_EvenWithMatchingUrl()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("secure-note!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("secure-note!");

        session.AddEntry(new VaultEntry
        {
            Title = "Secret note",
            Username = "note-user",
            Password = "note-secret",
            Url = "https://login.example.com/note",
            IsSecureNote = true
        });

        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.example.com",
            Url = "https://login.example.com/note"
        });
        Assert.Empty(listed.Matches!);

        session.AddEntry(new VaultEntry
        {
            Title = "Real login",
            Username = "user",
            Password = "pw",
            Url = "https://login.example.com"
        });

        var listedLogin = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "login.example.com",
            Url = "https://login.example.com/"
        });
        Assert.Single(listedLogin.Matches!);

        session.AddEntry(new VaultEntry
        {
            Title = "Hidden secure",
            Username = "hidden",
            Password = "hidden-pw",
            Url = "https://login.example.com/hidden",
            IsSecureNote = true
        });

        var resolve = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "login.example.com",
            Url = "https://login.example.com/hidden",
            EntryId = session.Context!.Payload.Entries.First(e => e.IsSecureNote && e.Title == "Hidden secure").Id
        });
        Assert.False(resolve.Found);
        Assert.Equal("no_match", resolve.Error);
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

        var multi = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "github.com",
            Url = "https://github.com/login"
        });
        Assert.False(multi.Found);
        Assert.Equal("multiple_matches", multi.Error);
        Assert.Equal(2, multi.Matches!.Count);

        var personal = multi.Matches!.First(m => m.Title == "Personal GitHub");
        var picked = ResolveWithNonce(session, new CredentialRequest
        {
            Domain = "github.com",
            Url = "https://github.com/login",
            EntryId = personal.Id
        });
        Assert.True(picked.Found);
        Assert.Equal("Personal GitHub", picked.Title);
        Assert.Equal("me@home.com", picked.Username);
    }

    [Fact]
    public void ResolveForDomain_RejectsUrlDomainMismatch()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("mismatch-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("mismatch-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "Site",
            Username = "user",
            Password = "secret",
            Url = "https://login.example.com"
        });

        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "evil.example.com",
            Url = "https://login.example.com/signin"
        });
        Assert.Equal("host_mismatch", listed.Error);

        var blocked = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "evil.example.com",
            Url = "https://login.example.com/signin",
            FillNonce = "00"
        });
        // The host inconsistency is rejected regardless of the (bogus) nonce.
        Assert.Equal("host_mismatch", blocked.Error);
    }

    [Fact]
    public void ResolveForDomain_RejectsNonceIssuedForDifferentHost()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("nonce-bind-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("nonce-bind-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "Bank",
            Username = "victim",
            Password = "super-secret",
            Url = "https://victim-bank.com"
        });

        // Attacker lists for an unrelated host and captures the issued nonce.
        var listed = session.ListMatchesForDomain(new CredentialRequest
        {
            Domain = "attacker.com",
            Url = "https://attacker.com"
        });
        Assert.NotNull(listed.FillNonce);

        // Replaying that nonce against the victim host must fail (nonce is host-bound).
        var replay = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "victim-bank.com",
            Url = "https://victim-bank.com",
            FillNonce = listed.FillNonce
        });
        Assert.Equal("invalid_nonce", replay.Error);
        Assert.True(string.IsNullOrEmpty(replay.Password));
    }

    [Fact]
    public void ResolveForDomain_RequiresFillNonce()
    {
        var engine = new VaultEngine(_dir, DpapiScope.CurrentUser);
        engine.CreateVault("nonce-test!", SecurityLevel.Standard);
        var session = new VaultSession(_dir, DpapiScope.CurrentUser);
        session.Unlock("nonce-test!");

        session.AddEntry(new VaultEntry
        {
            Title = "Site",
            Username = "user",
            Password = "secret",
            Url = "https://example.com"
        });

        var blocked = session.ResolveForDomain(new CredentialRequest
        {
            Domain = "example.com",
            Url = "https://example.com/login"
        });
        Assert.Equal("invalid_nonce", blocked.Error);
    }
}
