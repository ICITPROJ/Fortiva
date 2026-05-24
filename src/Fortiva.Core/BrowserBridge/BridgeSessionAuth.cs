using System.Security.Cryptography;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Per-unlock session token for browser bridge IPC. Held in process memory while vault is unlocked.
/// Legacy on-disk tokens are removed on unlock/lock.
/// </summary>
public static class BridgeSessionAuth
{
    private const string TokenFileName = "bridge.session";
    private static ReadOnlySpan<byte> DpapiEntropy => "Fortiva.Bridge.Session.v1"u8;
    private static string? _tokenDirectoryOverride;
    private static string? _inMemoryToken;
    private static readonly object TokenLock = new();

    public static void ConfigureTokenDirectory(string? directory)
        => _tokenDirectoryOverride = directory;

    public static string TokenDirectory =>
        _tokenDirectoryOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fortiva");

    public static string TokenPath => Path.Combine(TokenDirectory, TokenFileName);

    public static string CreateSessionToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        lock (TokenLock)
            _inMemoryToken = token;
        CryptographicOperations.ZeroMemory(tokenBytes);
        ClearLegacyTokenFile();
        return token;
    }

    public static void ClearSessionToken()
    {
        lock (TokenLock)
            _inMemoryToken = null;
        ClearLegacyTokenFile();
    }

    /// <summary>Returns the active in-process token (same process as Fortiva app).</summary>
    public static bool TryReadExpectedToken(out string token)
    {
        lock (TokenLock)
        {
            if (!string.IsNullOrEmpty(_inMemoryToken))
            {
                token = _inMemoryToken;
                return true;
            }
        }

        token = "";
        return false;
    }

    /// <summary>Request token from the unlocked Fortiva process via secured named pipe.</summary>
    public static async Task<string?> RequestTokenFromBrokerAsync(int timeoutMs = 2000)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(
                ".", BridgeTokenBroker.PipeName, System.IO.Pipes.PipeDirection.InOut);
            await client.ConnectAsync(timeoutMs);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8);
            await writer.WriteLineAsync("GET");
            return await reader.ReadLineAsync();
        }
        catch
        {
            return null;
        }
    }

    public static bool ValidateToken(string? provided, string expectedRawToken)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return FixedTimeEqualsUtf8(provided, expectedRawToken);
    }

    internal static bool FixedTimeEqualsUtf8(string a, string b)
    {
        var ba = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var bb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static void ClearLegacyTokenFile()
    {
        try
        {
            if (File.Exists(TokenPath))
                File.Delete(TokenPath);
        }
        catch { }
    }
}
