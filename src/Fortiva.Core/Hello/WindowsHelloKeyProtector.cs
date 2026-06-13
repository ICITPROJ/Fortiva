using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Hello;

/// <summary>
/// Protects a Hello unlock bundle with DPAPI (format v3).
/// UserConsentVerifier must succeed before storing; TryLoadMasterKey requires HelloVerificationGate.
/// For TPM-backed storage use HelloCredentialStore (format v4) via HelloUnlockManager.
/// </summary>
public sealed class WindowsHelloKeyProtector
{
    public static readonly byte[] MagicV3 = [0x46, 0x54, 0x57, 0x48, 0x03]; // "FTWH" + 0x03
    public static readonly byte[] MagicV4 = [0x46, 0x54, 0x57, 0x48, 0x04]; // "FTWH" + 0x04
    internal static ReadOnlySpan<byte> MkWrapAssociatedData => "Fortiva.Hello.MK.v1"u8;

    private readonly string _protectorPath;
    private readonly DataProtectionScope _scope;
    private byte[] _bindingEntropy;

    public WindowsHelloKeyProtector(string dataDirectory, bool machineScope = false)
    {
        Directory.CreateDirectory(dataDirectory);
        _protectorPath = Path.Combine(dataDirectory, "hello.keyprotect");
        _scope = DataProtectionScope.CurrentUser;
        _ = machineScope;
        _bindingEntropy = LoadExistingBindingEntropy(dataDirectory);
    }

    public bool IsConfigured => File.Exists(_protectorPath);

    public static bool IsHardwareBackedBundle(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "hello.keyprotect");
        if (!File.Exists(path))
            return false;

        try
        {
            var head = File.ReadAllBytes(path);
            return head.Length >= MagicV4.Length &&
                   head.AsSpan(0, MagicV4.Length).SequenceEqual(MagicV4);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Store Hello unlock material derived from the current session master key.</summary>
    public void StoreHelloBundle(ReadOnlySpan<byte> masterKey, bool helloVerified = true)
    {
        if (!helloVerified || !HelloVerificationGate.TryConsumeVerification())
            throw new InvalidOperationException("Windows Hello verification is required before storing Hello credentials.");

        EnsureBindingEntropy();
        var unlockKey = RandomNumberGenerator.GetBytes(32);
        byte[]? wrappedMk = null;
        try
        {
            wrappedMk = CngAesGcm.Seal(unlockKey, masterKey, MkWrapAssociatedData);
            var payload = new byte[MagicV3.Length + unlockKey.Length + wrappedMk.Length];
            MagicV3.CopyTo(payload, 0);
            unlockKey.CopyTo(payload.AsSpan(MagicV3.Length));
            wrappedMk.CopyTo(payload, MagicV3.Length + unlockKey.Length);
            var protectedBytes = ProtectedData.Protect(payload, BuildEntropy(helloVerified: true), _scope);
            HelloFileSecurity.WriteRestrictedFile(_protectorPath, protectedBytes);
        }
        finally
        {
            SecureMemory.Zero(unlockKey);
            if (wrappedMk is not null) SecureMemory.Zero(wrappedMk);
        }
    }

    /// <summary>Returns a copy of the master key for vault unlock, or null if missing/legacy/invalid.</summary>
    public byte[]? TryLoadMasterKey(bool helloVerified = true)
    {
        if (!helloVerified || !HelloVerificationGate.TryConsumeVerification())
            return null;

        if (!File.Exists(_protectorPath))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(_protectorPath);
            if (protectedBytes.Length >= MagicV4.Length &&
                protectedBytes.AsSpan(0, MagicV4.Length).SequenceEqual(MagicV4))
                return null;

            var plain = ProtectedData.Unprotect(protectedBytes, BuildEntropy(helloVerified: true), _scope);
            try
            {
                if (plain.Length <= MagicV3.Length + 32) return null;
                for (var i = 0; i < MagicV3.Length; i++)
                    if (plain[i] != MagicV3[i]) return null;

                var unlockKey = plain.AsSpan(MagicV3.Length, 32).ToArray();
                var wrappedMk = plain[(MagicV3.Length + 32)..];
                try
                {
                    return CngAesGcm.Open(unlockKey, wrappedMk, MkWrapAssociatedData);
                }
                finally
                {
                    SecureMemory.Zero(unlockKey);
                }
            }
            finally
            {
                SecureMemory.Zero(plain);
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        HelloFileSecurity.SecureDelete(_protectorPath);
        var bindingPath = Path.Combine(Path.GetDirectoryName(_protectorPath)!, "hello.binding");
        HelloFileSecurity.SecureDelete(bindingPath);
    }

    internal byte[] LoadBindingEntropy() => _bindingEntropy.ToArray();

    internal string ProtectorPath => _protectorPath;

    internal string BindingPath => Path.Combine(Path.GetDirectoryName(_protectorPath)!, "hello.binding");

    private byte[] BuildEntropy(bool helloVerified)
    {
        var label = helloVerified ? "Fortiva.Hello.Verified.v3" : "Fortiva.Hello.Pending.v3";
        var labelBytes = Encoding.UTF8.GetBytes(label);
        var combined = new byte[labelBytes.Length + _bindingEntropy.Length];
        labelBytes.CopyTo(combined, 0);
        _bindingEntropy.CopyTo(combined, labelBytes.Length);
        return combined;
    }

    private void EnsureBindingEntropy()
    {
        if (_bindingEntropy.Length > 0)
            return;

        var entropy = RandomNumberGenerator.GetBytes(32);
        HelloFileSecurity.WriteRestrictedFile(BindingPath, entropy);
        _bindingEntropy = entropy;
    }

    private static byte[] LoadExistingBindingEntropy(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "hello.binding");
        return File.Exists(path) ? File.ReadAllBytes(path) : [];
    }
}
