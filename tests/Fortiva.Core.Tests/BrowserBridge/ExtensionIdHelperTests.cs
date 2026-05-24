using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class ExtensionIdHelperTests
{
    private const string ManifestKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEApgPHScbVbaCAFCslR8Uny0DNaUkISAkElV4tPeClgB9x3U6Um50R1wnhQjXkBQqY87nYv4B5WQcA8E+YGkzf0fViVz2mYzw4ctesC8ApiQX6EvdH4UCGE+fMc3CARfCSX/epMyiHuVAj5wrodxoxSHnlfohbuxOdzWRfp+RFiaqdFos1+S4Eia3F98BWymIQdK1PY+6ifAxG7aiYPs72Nbm4YGxs/Y3RA1ar3s/itgclpvs5gZHUQenLQHE7f/vSJkKN5onvBNVwJjlE9J94DYEjZDd1vUUGQ8+LBhGztgLUpS2Y4XdBmwrVjER/yXUx5KTAMI1cqycWaZqBIR9R2QIDAQAB";

    [Fact]
    public void ComputeFromManifestKey_Produces32CharId()
    {
        var id = ExtensionIdHelper.ComputeFromManifestKey(ManifestKey);
        Assert.Equal(32, id.Length);
        Assert.Matches("^[a-p]{32}$", id);
    }

    [Fact]
    public void ReadFromManifestFile_MatchesComputedKey()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "extension", "manifest.json"));

        var fromFile = ExtensionIdHelper.ReadFromManifestFile(manifestPath);
        var fromKey = ExtensionIdHelper.ComputeFromManifestKey(ManifestKey);
        Assert.Equal(fromKey, fromFile);
    }
}
