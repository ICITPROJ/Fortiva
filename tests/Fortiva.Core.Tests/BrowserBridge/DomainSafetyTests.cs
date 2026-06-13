using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class DomainSafetyTests
{
    [Theory]
    [InlineData("login.ionos.co.uk", "www.ionos.co.uk", true)]
    [InlineData("login.ionos.co.uk", "ionos.co.uk", true)]
    [InlineData("notexample.com", "example.com", false)]
    [InlineData("192.168.0.45", "192.168.0.45", true)]
    [InlineData("192.168.0.45", "192.168.1.41", false)]
    [InlineData("github.com", "www.github.com", true)]
    public void HostsMatchForAutofill_UsesRegistrableDomain(string a, string b, bool expected)
        => Assert.Equal(expected, DomainSafety.HostsMatchForAutofill(a, b));

    [Fact]
    public void GetRegistrableDomain_HandlesCoUk()
        => Assert.Equal("ionos.co.uk", DomainSafety.GetRegistrableDomain("login.ionos.co.uk"));

    [Theory]
    [InlineData("login.ionos.co.uk", "login.ionos.co.uk", true)]
    [InlineData("login.ionos.co.uk", "www.ionos.co.uk", false)]
    public void HostsMatchForCredentialRelease_RequiresExactHost(string a, string b, bool expected)
        => Assert.Equal(expected, DomainSafety.HostsMatchForCredentialRelease(a, b));

    [Fact]
    public void ContainsSuspiciousCharacters_FlagsPureCyrillicHost()
        => Assert.True(DomainSafety.ContainsSuspiciousCharacters("пример.рф"));

    [Theory]
    [InlineData("xn--paypal-abc.com", true)]
    [InlineData("login.ionos.co.uk", false)]
    public void ContainsAceEncodedLabel_DetectsPunycode(string host, bool expected)
        => Assert.Equal(expected, DomainSafety.ContainsAceEncodedLabel(host));

    [Fact]
    public void ContainsSuspiciousCharacters_RejectsAceLabels()
        => Assert.True(DomainSafety.ContainsSuspiciousCharacters("xn--example-9ta.com"));
}
