using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fortiva.Core.Licensing;

public sealed class LicenseDocument
{
    public string Edition { get; set; } = "Enterprise";
    public DateTimeOffset ExpiresAt { get; set; }
    public string CompanyName { get; set; } = "";
    public List<string> FeatureFlags { get; set; } = ["vault", "policy", "audit", "shared_vaults"];
    public int MaxSeats { get; set; } = 100;
}

public sealed class SignedLicense
{
    public LicenseDocument Document { get; set; } = new();
    public byte[] Signature { get; set; } = [];
}

public static class LicensePaths
{
    public static string LicenseFilePath =>
        Path.Combine(Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva"), "license.dat");
}

public static class LicenseStore
{
    public static void Save(SignedLicense license)
    {
        var dir = Path.GetDirectoryName(LicensePaths.LicenseFilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.SerializeToUtf8Bytes(license);
        var protectedBytes = ProtectedData.Protect(json, "Fortiva.License.v1"u8.ToArray(), DataProtectionScope.LocalMachine);
        File.WriteAllBytes(LicensePaths.LicenseFilePath, protectedBytes);
    }

    public static SignedLicense? Load()
    {
        if (!File.Exists(LicensePaths.LicenseFilePath)) return null;
        return LoadProtectedBytes(File.ReadAllBytes(LicensePaths.LicenseFilePath));
    }

    /// <summary>Import from portable .json or DPAPI-protected .dat (same format as license.dat).</summary>
    public static SignedLicense? TryImportFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".dat", StringComparison.OrdinalIgnoreCase))
            return LoadProtectedBytes(File.ReadAllBytes(filePath));

        var json = File.ReadAllText(filePath);
        var license = JsonSerializer.Deserialize<SignedLicense>(json);
        if (license is null) return null;
        return LicenseVerifier.Verify(license) ? license : null;
    }

    private static SignedLicense? LoadProtectedBytes(byte[] protectedBytes)
    {
        try
        {
            var json = ProtectedData.Unprotect(protectedBytes, "Fortiva.License.v1"u8.ToArray(), DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<SignedLicense>(json);
        }
        catch
        {
            return null;
        }
    }
}

public static class LicenseVerifier
{
    /// <summary>
    /// Verifies RSA-PKCS1 signature over canonical JSON payload. Public key embedded for offline verification.
    /// Production deployments replace EmbeddedPublicKeyXml with org-specific key from Admin Console issuance.
    /// </summary>
    public static bool Verify(SignedLicense license, string? publicKeyXml = null)
    {
        if (license.Signature.Length == 0) return false;
        var payload = CanonicalPayload(license.Document);
        try
        {
            using var rsa = RSA.Create();
            var keyXml = publicKeyXml ?? EmbeddedPublicKeyXml;
            rsa.FromXmlString(keyXml);
            return rsa.VerifyData(payload, license.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidAndNotExpired(SignedLicense? license, string? publicKeyXml = null)
    {
        if (license is null) return false;
        if (!Verify(license, publicKeyXml)) return false;
        return license.Document.ExpiresAt > DateTimeOffset.UtcNow;
    }

    /// <summary>True when the embedded public key is the known development key shipped in source.</summary>
    public static bool UsesKnownDevelopmentPublicKey =>
        EmbeddedPublicKeyXml.Contains("nuA+R1rT7Q7lUkNhM9IMcztv", StringComparison.Ordinal);

    public static void EnsureProductionKeyForEnterpriseBuild()
    {
#if !DEBUG
        if (string.Equals(
                Environment.GetEnvironmentVariable("FORTIVA_ALLOW_DEV_LICENSE_KEY"),
                "1",
                StringComparison.Ordinal))
            return;

        if (UsesKnownDevelopmentPublicKey)
            throw new InvalidOperationException(
                "This Enterprise/Admin build embeds the development license public key. " +
                "Replace EmbeddedPublicKeyXml with your organization key before deployment.");
#endif
    }

    public static byte[] CanonicalPayload(LicenseDocument doc)
    {
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>Development 2048-bit RSA public key. Replace in production builds via LicenseTool generate-key.</summary>
    internal const string EmbeddedPublicKeyXml =
        "<RSAKeyValue><Modulus>nuA+R1rT7Q7lUkNhM9IMcztv+V0/XhyOfYLotZlCIRK3nngAOUTumnsh06O7TnMJBEW6aJK52J1GI6Xp9bmJfo0fg3KguPADSM9POkcV68UtWf3sMkyukU6DlUzF39t/VnrncFuiHCg/DdRs1nuSWBrmtdLCsSpvD9KYstjvuyj95HrLgEVQpcU3y+ryXk9AiMBcPDnX61x4uoGzQxg30C8QTM71UOEfe98GUzx9gaqDc8nfh2s9ulC+PIeOZ7gF3K6BLbwCpp1DEMqehzJe0FrQfFDR41jwBPsiKHOyuKdDQP34Sz8KXCHMm1DDkM6tN8n7vpULlqO1DXtS7D1+1Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public static SignedLicense CreateDevLicense(string company, DateTimeOffset expires)
    {
        return new SignedLicense
        {
            Document = new LicenseDocument
            {
                CompanyName = company,
                ExpiresAt = expires,
                Edition = "Enterprise"
            },
            Signature = [] // Dev mode: verifier returns false unless test harness signs
        };
    }
}
