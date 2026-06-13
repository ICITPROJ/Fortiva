using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// AES-256-GCM protection for usernames and passwords on the credential named pipe.
/// Key is derived from the per-unlock session token (held only by the bridge host after broker fetch).
/// </summary>
public static class BridgeCredentialProtector
{
    private const byte FormatVersion = 1;
    private static ReadOnlySpan<byte> PasswordKdfContext => "Fortiva.Bridge.CredentialPipe.Password.v1"u8;
    private static ReadOnlySpan<byte> UsernameKdfContext => "Fortiva.Bridge.CredentialPipe.Username.v1"u8;

    public static CredentialResponse ProtectForPipe(CredentialResponse response, string sessionToken)
    {
        if (!response.Found)
            return response;

        if (!string.IsNullOrEmpty(response.Username))
        {
            response.UsernameSealed = Convert.ToBase64String(
                SealUtf8(response.Username, sessionToken, UsernameKdfContext));
            response.Username = "";
            response.UsernameProtected = true;
        }

        if (!string.IsNullOrEmpty(response.Password))
        {
            response.PasswordSealed = Convert.ToBase64String(
                SealUtf8(response.Password, sessionToken, PasswordKdfContext));
            response.Password = "";
            response.PasswordProtected = true;
        }

        return response;
    }

    public static CredentialResponse ProtectListForPipe(CredentialResponse response, string sessionToken)
    {
        if (response.Matches is null || response.Matches.Count == 0)
            return response;

        var sealedMatches = new List<CredentialMatchSummary>(response.Matches.Count);
        foreach (var match in response.Matches)
        {
            var copy = new CredentialMatchSummary
            {
                Id = match.Id,
                Title = match.Title,
                Releasable = match.Releasable
            };

            if (!string.IsNullOrEmpty(match.Username))
            {
                copy.UsernameSealed = Convert.ToBase64String(
                    SealUtf8(match.Username, sessionToken, UsernameKdfContext));
                copy.Username = "";
                copy.UsernameProtected = true;
            }

            sealedMatches.Add(copy);
        }

        response.Matches = sealedMatches;
        return response;
    }

    public static CredentialResponse UnprotectFromPipe(CredentialResponse response, string sessionToken)
    {
        UnprotectMatchUsernames(response.Matches, sessionToken);

        if (response.UsernameProtected && !string.IsNullOrEmpty(response.UsernameSealed))
        {
            response.Username = UnsealUtf8(response.UsernameSealed, sessionToken, UsernameKdfContext);
            response.UsernameProtected = false;
            response.UsernameSealed = null;
        }

        if (response.PasswordProtected && !string.IsNullOrEmpty(response.PasswordSealed))
        {
            response.Password = UnsealUtf8(response.PasswordSealed, sessionToken, PasswordKdfContext);
            response.PasswordProtected = false;
            response.PasswordSealed = null;
        }

        return response;
    }

    private static void UnprotectMatchUsernames(IReadOnlyList<CredentialMatchSummary>? matches, string sessionToken)
    {
        if (matches is null)
            return;

        foreach (var match in matches)
        {
            if (!match.UsernameProtected || string.IsNullOrEmpty(match.UsernameSealed))
                continue;

            match.Username = UnsealUtf8(match.UsernameSealed, sessionToken, UsernameKdfContext);
            match.UsernameProtected = false;
            match.UsernameSealed = null;
        }
    }

    public static string UnprotectJsonLine(string jsonLine, string sessionToken)
    {
        var response = BridgeJson.Deserialize<CredentialResponse>(jsonLine);
        if (response is null)
            return jsonLine;

        var hasProtected = response.PasswordProtected || response.UsernameProtected
            || response.Matches?.Any(m => m.UsernameProtected) == true;
        if (!hasProtected)
            return jsonLine;

        try
        {
            return BridgeJson.Serialize(UnprotectFromPipe(response, sessionToken));
        }
        catch (CryptographicException)
        {
            return BridgeJson.Serialize(new CredentialResponse { Error = "decrypt_failed" });
        }
    }

    private static byte[] SealUtf8(string plaintext, string sessionToken, ReadOnlySpan<byte> kdfContext)
    {
        var key = DeriveKey(sessionToken, kdfContext);
        var nonce = CngAesGcm.GenerateNonce();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var (cipher, tag) = CngAesGcm.Encrypt(key, nonce, plainBytes, kdfContext);
            var blob = new byte[1 + nonce.Length + tag.Length + cipher.Length];
            blob[0] = FormatVersion;
            Buffer.BlockCopy(nonce, 0, blob, 1, nonce.Length);
            Buffer.BlockCopy(tag, 0, blob, 1 + nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, blob, 1 + nonce.Length + tag.Length, cipher.Length);
            return blob;
        }
        finally
        {
            SecureMemory.Zero(plainBytes);
            SecureMemory.Zero(key);
        }
    }

    private static string UnsealUtf8(string sealedBase64, string sessionToken, ReadOnlySpan<byte> kdfContext)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(sealedBase64);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid sealed credential encoding.", ex);
        }

        if (blob.Length < 1 + CngAesGcm.NonceSizeBytes + CngAesGcm.TagSizeBytes || blob[0] != FormatVersion)
            throw new CryptographicException("Invalid sealed credential blob.");

        var key = DeriveKey(sessionToken, kdfContext);
        try
        {
            var nonce = blob.AsSpan(1, CngAesGcm.NonceSizeBytes);
            var tag = blob.AsSpan(1 + CngAesGcm.NonceSizeBytes, CngAesGcm.TagSizeBytes);
            var cipher = blob.AsSpan(1 + CngAesGcm.NonceSizeBytes + CngAesGcm.TagSizeBytes);
            var plain = CngAesGcm.Decrypt(key, nonce, cipher, tag, kdfContext);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Credential decrypt failed.", ex);
        }
        finally
        {
            SecureMemory.Zero(key);
        }
    }

    private static byte[] DeriveKey(string sessionToken, ReadOnlySpan<byte> kdfContext)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(sessionToken);
        try
        {
            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                tokenBytes,
                CngAesGcm.KeySizeBytes,
                salt: Array.Empty<byte>(),
                info: kdfContext.ToArray());
        }
        finally
        {
            SecureMemory.Zero(tokenBytes);
        }
    }
}
