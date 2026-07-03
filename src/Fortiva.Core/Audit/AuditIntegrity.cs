using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fortiva.Core.Audit;

/// <summary>HMAC-SHA256 integrity for audit JSONL lines (tamper detection).</summary>
internal static class AuditIntegrity
{
    private const string KeyEntropy = "Fortiva.Audit.HMAC.v1";

    public static string SignLine(string jsonLine, byte[] hmacKey)
    {
        var mac = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(jsonLine));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static bool VerifyLine(string jsonLine, string? signatureHex, byte[] hmacKey)
    {
        if (string.IsNullOrWhiteSpace(signatureHex) || signatureHex.Length != 64)
            return false;
        var expected = SignLine(jsonLine, hmacKey);
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(signatureHex),
                Convert.FromHexString(expected));
        }
        catch
        {
            return false;
        }
    }

    public static byte[] LoadOrCreateHmacKey(string auditDirectory, DataProtectionScope scope = DataProtectionScope.LocalMachine)
    {
        Directory.CreateDirectory(auditDirectory);
        var keyPath = Path.Combine(auditDirectory, ".audit.hmac.key");
        var entropy = Encoding.UTF8.GetBytes(KeyEntropy);
        if (File.Exists(keyPath))
        {
            var protectedBytes = File.ReadAllBytes(keyPath);
            try
            {
                return ProtectedData.Unprotect(protectedBytes, entropy, scope);
            }
            catch (CryptographicException) when (scope == DataProtectionScope.CurrentUser)
            {
                // Migrate keys created before Personal audit switched to CurrentUser DPAPI.
                var key = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.LocalMachine);
                var protectedKey = ProtectedData.Protect(key, entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyPath, protectedKey);
                return key;
            }
        }

        var newKey = RandomNumberGenerator.GetBytes(32);
        var protectedNewKey = ProtectedData.Protect(newKey, entropy, scope);
        File.WriteAllBytes(keyPath, protectedNewKey);
        return newKey;
    }
}
