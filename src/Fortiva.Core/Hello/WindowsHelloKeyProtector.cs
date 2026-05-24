using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Hello;

/// <summary>
/// Protects a Hello unlock bundle with DPAPI. Never stores the master password.
/// Format v3 (plaintext before DPAPI):
///   [FTWH magic][0x03][32-byte unlock key][wrapped master key blob]
/// </summary>
public sealed class WindowsHelloKeyProtector
{
    private static readonly byte[] MagicV3 = [0x46, 0x54, 0x57, 0x48, 0x03]; // "FTWH" + 0x03
    private static ReadOnlySpan<byte> MkWrapAssociatedData => "Fortiva.Hello.MK.v1"u8;

    private readonly string _protectorPath;
    private readonly DataProtectionScope _scope;
    private readonly byte[] _bindingEntropy;

    public WindowsHelloKeyProtector(string dataDirectory, bool machineScope = false)
    {
        Directory.CreateDirectory(dataDirectory);
        _protectorPath = Path.Combine(dataDirectory, "hello.keyprotect");
        // Hello material is always per-user; never use LocalMachine DPAPI (cross-user decrypt on shared PCs).
        _scope = DataProtectionScope.CurrentUser;
        _ = machineScope; // retained for call-site compatibility
        _bindingEntropy = LoadOrCreateBindingEntropy(dataDirectory);
    }

    public bool IsConfigured => File.Exists(_protectorPath);

    /// <summary>Store Hello unlock material derived from the current session master key.</summary>
    public void StoreHelloBundle(ReadOnlySpan<byte> masterKey, bool helloVerified = false)
    {
        var unlockKey = RandomNumberGenerator.GetBytes(32);
        byte[]? wrappedMk = null;
        try
        {
            wrappedMk = CngAesGcm.Seal(unlockKey, masterKey, MkWrapAssociatedData);
            var payload = new byte[MagicV3.Length + unlockKey.Length + wrappedMk.Length];
            MagicV3.CopyTo(payload, 0);
            unlockKey.CopyTo(payload.AsSpan(MagicV3.Length));
            wrappedMk.CopyTo(payload, MagicV3.Length + unlockKey.Length);
            var protectedBytes = ProtectedData.Protect(payload, BuildEntropy(helloVerified), _scope);
            File.WriteAllBytes(_protectorPath, protectedBytes);
        }
        finally
        {
            SecureMemory.Zero(unlockKey);
            if (wrappedMk is not null) SecureMemory.Zero(wrappedMk);
        }
    }

    /// <summary>Returns a copy of the master key for vault unlock, or null if missing/legacy/invalid.</summary>
    public byte[]? TryLoadMasterKey(bool helloVerified = false)
    {
        if (!File.Exists(_protectorPath)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(_protectorPath);
            var plain = ProtectedData.Unprotect(protectedBytes, BuildEntropy(helloVerified), _scope);
            try
            {
                if (plain.Length <= MagicV3.Length + 32) return null;
                for (var i = 0; i < MagicV3.Length; i++)
                    if (plain[i] != MagicV3[i]) return null;

                var unlockKey = plain.AsSpan(MagicV3.Length, 32).ToArray();
                var wrappedMk = plain[(MagicV3.Length + 32)..];
                try
                {
                    var mk = CngAesGcm.Open(unlockKey, wrappedMk, MkWrapAssociatedData);
                    return mk;
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
        if (File.Exists(_protectorPath)) File.Delete(_protectorPath);
        var bindingPath = Path.Combine(Path.GetDirectoryName(_protectorPath)!, "hello.binding");
        if (File.Exists(bindingPath)) File.Delete(bindingPath);
    }

    private byte[] BuildEntropy(bool helloVerified)
    {
        var label = helloVerified ? "Fortiva.Hello.Verified.v3" : "Fortiva.Hello.Pending.v3";
        var labelBytes = Encoding.UTF8.GetBytes(label);
        var combined = new byte[labelBytes.Length + _bindingEntropy.Length];
        labelBytes.CopyTo(combined, 0);
        _bindingEntropy.CopyTo(combined, labelBytes.Length);
        return combined;
    }

    private static byte[] LoadOrCreateBindingEntropy(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "hello.binding");
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        var entropy = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, entropy);
        return entropy;
    }
}
