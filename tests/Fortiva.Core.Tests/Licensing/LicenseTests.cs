using System.Security.Cryptography;
using Fortiva.Core.Licensing;

namespace Fortiva.Core.Tests.Licensing;

public class LicenseTests
{
    [Fact]
    public void SignVerify_WithSameKey_Succeeds()
    {
        using var rsa = RSA.Create(2048);
        var pubKeyXml = rsa.ToXmlString(includePrivateParameters: false);

        var doc = new LicenseDocument
        {
            CompanyName = "Acme Corp",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(365),
            Edition = "Enterprise"
        };
        var payload = LicenseVerifier.CanonicalPayload(doc);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var signed = new SignedLicense { Document = doc, Signature = signature };
        Assert.True(LicenseVerifier.Verify(signed, pubKeyXml));
        Assert.True(LicenseVerifier.IsValidAndNotExpired(signed, pubKeyXml));
    }

    [Fact]
    public void TamperedSignature_FailsVerification()
    {
        using var rsa = RSA.Create(2048);
        var pubKeyXml = rsa.ToXmlString(includePrivateParameters: false);
        var doc = new LicenseDocument { CompanyName = "Acme", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var payload = LicenseVerifier.CanonicalPayload(doc);
        var sig = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        sig[^1] ^= 0xFF;
        Assert.False(LicenseVerifier.Verify(new SignedLicense { Document = doc, Signature = sig }, pubKeyXml));
    }

    [Fact]
    public void ExpiredLicense_FailsIsValidCheck()
    {
        using var rsa = RSA.Create(2048);
        var pubKeyXml = rsa.ToXmlString(includePrivateParameters: false);
        var doc = new LicenseDocument { CompanyName = "Acme", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var payload = LicenseVerifier.CanonicalPayload(doc);
        var sig = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signed = new SignedLicense { Document = doc, Signature = sig };
        Assert.True(LicenseVerifier.Verify(signed, pubKeyXml));
        Assert.False(LicenseVerifier.IsValidAndNotExpired(signed, pubKeyXml));
    }

    [Fact]
    public void TryImportFromFile_ReadsPortableJson()
    {
        using var rsa = RSA.Create(2048);
        var doc = new LicenseDocument
        {
            CompanyName = "Import Test",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            Edition = "Enterprise"
        };
        var payload = LicenseVerifier.CanonicalPayload(doc);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signed = new SignedLicense { Document = doc, Signature = signature };

        var path = Path.Combine(Path.GetTempPath(), $"fortiva-lic-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(signed));

        var imported = LicenseStore.TryImportFromFile(path);
        Assert.NotNull(imported);
        Assert.Equal("Import Test", imported!.Document.CompanyName);
    }
}
