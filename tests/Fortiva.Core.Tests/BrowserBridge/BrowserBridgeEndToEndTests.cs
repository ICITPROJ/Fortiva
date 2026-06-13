using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;
using Fortiva.Core.Services;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public sealed class BrowserBridgeEndToEndTests : IDisposable
{
    private readonly string _vaultDir;

    public BrowserBridgeEndToEndTests()
    {
        AuthenticodePolicy.ConfigureForEdition("Personal");
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
        BridgeClientValidator.ConfigureAllowedInstallRoots(AppContext.BaseDirectory);
        _vaultDir = Path.Combine(Path.GetTempPath(), "fv-bridge-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultDir);
    }

    public void Dispose()
    {
        BridgeTestEnvironment.EnsurePipeNamesAvailable();
        try { if (Directory.Exists(_vaultDir)) Directory.Delete(_vaultDir, true); } catch { }
    }

    [Fact]
    public void UnlockedSession_BridgePipesHealthy_AndListsIonosCredentials()
    {
        BridgeTestEnvironment.PrepareExclusivePipes();
        VaultSession? session = null;
        try
        {
            session = CreateUnlockedSessionWithIonosEntry();
            session.EnsureBridgeInfrastructureHealthy();

            var healthy = false;
            for (var i = 0; i < 80; i++)
            {
                if (session.IsBridgeHealthy())
                {
                    healthy = true;
                    break;
                }

                if (i == 20)
                    session.EnsureBridgeInfrastructureHealthy();

                Thread.Sleep(100);
            }

            Assert.True(healthy, "Token and credential pipes must both be listening after unlock.");

            var listed = session.ListMatchesForDomain(new CredentialRequest
            {
                Domain = "login.ionos.co.uk",
                Url = "https://login.ionos.co.uk/"
            });

            Assert.NotNull(listed.Matches);
            Assert.Single(listed.Matches!);
            Assert.Equal("user@example.com", listed.Matches![0].Username);
            Assert.False(string.IsNullOrEmpty(listed.FillNonce));
        }
        finally
        {
            session?.Dispose();
            BridgeTestEnvironment.EnsurePipeNamesAvailable();
        }
    }

    [Fact]
    public void VaultSession_MatchesIonos_FromTitleOnlyUrl()
    {
        BridgeTestEnvironment.PrepareExclusivePipes();
        VaultSession? session = null;
        try
        {
            session = new VaultSession(_vaultDir, Core.LocalState.DpapiScope.CurrentUser);
            session.CreateVault("bridge-title-url!", SecurityLevel.Standard);
            session.Unlock("bridge-title-url!");
            session.AddEntry(new VaultEntry
            {
                Title = "login.ionos.co.uk",
                Username = "title-only@example.com",
                Password = "pw"
            });

            var listed = session.ListMatchesForDomain(new CredentialRequest
            {
                Domain = "login.ionos.co.uk",
                Url = "https://login.ionos.co.uk/"
            });

            Assert.NotNull(listed.Matches);
            Assert.Single(listed.Matches!);
            Assert.Equal("title-only@example.com", listed.Matches![0].Username);
        }
        finally
        {
            session?.Dispose();
            BridgeTestEnvironment.EnsurePipeNamesAvailable();
        }
    }

    private VaultSession CreateUnlockedSessionWithIonosEntry()
    {
        var session = new VaultSession(_vaultDir, Core.LocalState.DpapiScope.CurrentUser);
        session.CreateVault("bridge-e2e-test!", SecurityLevel.Standard);
        session.Unlock("bridge-e2e-test!");
        session.AddEntry(new VaultEntry
        {
            Title = "IONOS",
            Username = "user@example.com",
            Password = "secret",
            Url = "https://login.ionos.co.uk/"
        });
        return session;
    }
}
