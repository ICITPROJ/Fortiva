using System.Security.Cryptography;

namespace Fortiva.Core.Crypto;

/// <summary>
/// AES-256-GCM via .NET (Windows CNG backend). Nonce is 12 bytes; tag is 16 bytes.
/// </summary>
public static class CngAesGcm
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] GenerateKey()
    {
        var key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    public static byte[] GenerateNonce()
    {
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    public static (byte[] ciphertext, byte[] tag) Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key, TagSizeBytes);
        if (associatedData.IsEmpty)
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        else
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData = default)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSizeBytes);
        if (associatedData.IsEmpty)
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        else
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    /// <summary>Encrypts and returns nonce || ciphertext || tag.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        var nonce = GenerateNonce();
        var (ct, tag) = Encrypt(key, nonce, plaintext, associatedData);
        var result = new byte[NonceSizeBytes + ct.Length + TagSizeBytes];
        nonce.CopyTo(result.AsSpan(0, NonceSizeBytes));
        ct.CopyTo(result.AsSpan(NonceSizeBytes));
        tag.CopyTo(result.AsSpan(NonceSizeBytes + ct.Length));
        return result;
    }

    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedBlob, ReadOnlySpan<byte> associatedData = default)
    {
        if (sealedBlob.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Sealed blob too short.");
        var nonce = sealedBlob[..NonceSizeBytes];
        var tagStart = sealedBlob.Length - TagSizeBytes;
        var ciphertext = sealedBlob[NonceSizeBytes..tagStart];
        var tag = sealedBlob[tagStart..];
        return Decrypt(key, nonce, ciphertext, tag, associatedData);
    }
}
