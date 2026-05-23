using System.Security.Cryptography;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Per-unlock session token for browser bridge IPC. Stored DPAPI-protected while vault is unlocked.
/// </summary>
public static class BridgeSessionAuth
{
    private const string TokenFileName = "bridge.session";
    private static ReadOnlySpan<byte> DpapiEntropy => "Fortiva.Bridge.Session.v1"u8;
    private static string? _tokenDirectoryOverride;

    public static void ConfigureTokenDirectory(string directory)
        => _tokenDirectoryOverride = directory;

    public static string TokenDirectory =>
        _tokenDirectoryOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fortiva");

    public static string TokenPath => Path.Combine(TokenDirectory, TokenFileName);

    public static string CreateSessionToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        Directory.CreateDirectory(TokenDirectory);
        var protectedBytes = ProtectedData.Protect(tokenBytes, DpapiEntropy.ToArray(), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, protectedBytes);
        CryptographicOperations.ZeroMemory(tokenBytes);
        return token;
    }

    public static void ClearSessionToken()
    {
        try
        {
            if (File.Exists(TokenPath))
                File.Delete(TokenPath);
        }
        catch { }
    }

    public static bool TryReadExpectedToken(out string token)
    {
        token = "";
        if (!File.Exists(TokenPath)) return false;
        try
        {
            var protectedBytes = File.ReadAllBytes(TokenPath);
            var plain = ProtectedData.Unprotect(protectedBytes, DpapiEntropy.ToArray(), DataProtectionScope.CurrentUser);
            try
            {
                token = Convert.ToBase64String(plain);
                return token.Length >= 16;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool ValidateToken(string? provided, string expectedRawToken)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return FixedTimeEqualsUtf8(provided, expectedRawToken);
    }

    internal static bool FixedTimeEqualsUtf8(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
