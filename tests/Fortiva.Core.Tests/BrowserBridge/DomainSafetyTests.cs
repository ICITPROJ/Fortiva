using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class DomainSafetyTests
{
    [Fact]
    public void NormalizeHost_LowercasesHost()
    {
        Assert.Equal("github.com", DomainSafety.NormalizeHost("GitHub.COM"));
    }

    [Fact]
    public void ContainsSuspiciousCharacters_RejectsMixedScript()
    {
        Assert.True(DomainSafety.ContainsSuspiciousCharacters("gооgle.com"));
    }

    [Fact]
    public void DisplayHost_ReturnsAsciiForUnicode()
    {
        var ascii = DomainSafety.DisplayHost("münchen.de");
        Assert.Contains("xn--", ascii, StringComparison.OrdinalIgnoreCase);
    }
}
