using Fortiva.Core.Updates;



namespace Fortiva.Core.Tests.Updates;



public sealed class UpdateUrlPolicyTests

{

    [Theory]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json")]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/download/v1.0.1/latest.personal.json")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/abc/latest.personal.json")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/00cd3fea-a25d-4fa7-9a74-703133da9ad8?rscd=attachment%3B+filename%3Dlatest.personal.json")]

    [InlineData("https://raw.githubusercontent.com/ICITPROJ/Fortiva/main/packaging/releases/latest.personal.json")]

    [InlineData("https://studio.icmclab.cloud/fortiva/releases/latest.personal.json")]

    public void ValidateManifestUrl_accepts_allowed_urls(string url)

        => UpdateUrlPolicy.ValidateManifestUrl(url);



    [Fact]
    public void IsLegacyFeedActive_respects_sunset_date()
    {
        Assert.True(UpdateUrlPolicy.IsLegacyFeedActive(UpdateUrlPolicy.LegacyFeedSunsetUtc.AddDays(-1)));
        Assert.False(UpdateUrlPolicy.IsLegacyFeedActive(UpdateUrlPolicy.LegacyFeedSunsetUtc.AddDays(1)));
    }



    [Theory]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/abc/evil.json")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/abc/FortivaPersonal-1.0.0-Setup.exe")]

    public void ValidateManifestUrl_rejects_wrong_cdn_asset(string url)

    {

        Assert.ThrowsAny<InvalidOperationException>(() => UpdateUrlPolicy.ValidateManifestUrl(url));

    }



    [Theory]

    [InlineData("https://github.com/evil/other/releases/latest/download/latest.personal.json")]

    [InlineData("https://github.com/ICITPROJ/az-700-prep/releases/latest/download/latest.personal.json")]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/latest/download/evil.json")]

    [InlineData("http://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json")]

    public void ValidateManifestUrl_rejects_untrusted_urls(string url)

    {

        Assert.ThrowsAny<InvalidOperationException>(() => UpdateUrlPolicy.ValidateManifestUrl(url));

    }



    [Theory]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/download/v1.0.0/FortivaPersonal-1.0.0-Setup.exe")]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/download/v2.1.3/FortivaPersonal-2.1.3-Setup.exe")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/abc/FortivaPersonal-1.0.13-Setup.exe")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/e8f20f3c-82af-4000-b19f-7a52eeace44b?rscd=attachment%3B+filename%3DFortivaPersonal-1.0.22-Setup.exe")]

    [InlineData("https://studio.icmclab.cloud/fortiva/releases/1.0.0/FortivaPersonal-1.0.0-Setup.exe")]

    public void ValidateInstallerUrl_accepts_allowed_urls(string url)

        => UpdateUrlPolicy.ValidateInstallerUrl(url);



    [Theory]

    [InlineData("https://github.com/evil/other/releases/download/v1.0.0/FortivaPersonal-1.0.0-Setup.exe")]

    [InlineData("https://github.com/ICITPROJ/Fortiva/releases/download/v1.0.0/FortivaEnterprise-1.0.0-Setup.exe")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/abc/latest.personal.json")]

    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1247749405/deadbeef-dead-beef-dead-beefdeadbeef?rscd=attachment%3B+filename%3Devil.exe")]

    public void ValidateInstallerUrl_rejects_untrusted_urls(string url)

    {

        Assert.ThrowsAny<InvalidOperationException>(() => UpdateUrlPolicy.ValidateInstallerUrl(url));

    }

}


