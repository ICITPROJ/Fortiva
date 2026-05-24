using System.Text.Json.Serialization;
using Fortiva.Core.Crypto;
using Fortiva.Core.Vault;
using Microsoft.Win32;

namespace Fortiva.Core.Policy;

public enum ClipboardPolicyMode
{
    Allowed,
    TimeoutOnly,
    Disabled
}

public enum ExportPolicyMode
{
    EncryptedOnly,
    NoPlaintext,
    PlaintextWithWarning
}

public sealed class FortivaPolicy
{
    public int MinArgon2MemoryKb { get; set; } = 65536;
    public int MinArgon2Iterations { get; set; } = 3;
    public int MinArgon2Parallelism { get; set; } = 4;
    public int MaxAutoLockSeconds { get; set; } = 300;
    public ClipboardPolicyMode ClipboardMode { get; set; } = ClipboardPolicyMode.TimeoutOnly;
    public int ClipboardClearSeconds { get; set; } = 30;
    public bool ClipboardDisabled { get; set; }
    public ExportPolicyMode ExportMode { get; set; } = ExportPolicyMode.EncryptedOnly;
    public bool PortableModeAllowed { get; set; } = true;
    public bool MandatoryParanoiaMode { get; set; }
    public bool MandatoryWindowsHello { get; set; }
    /// <summary>Allow TOTP / authenticator codes on vault entries (Enterprise).</summary>
    public bool TotpEnabled { get; set; }
    public bool ParanoiaModeRequired => MandatoryParanoiaMode;

    public Argon2Parameters MinimumKdf => new(
        MinArgon2MemoryKb,
        MinArgon2Iterations,
        MinArgon2Parallelism);

    public static FortivaPolicy PersonalDefault => new();

    public static FortivaPolicy StrictEnterprise => new()
    {
        MinArgon2MemoryKb = 131072,
        MinArgon2Iterations = 4,
        MinArgon2Parallelism = 4,
        MaxAutoLockSeconds = 120,
        ClipboardMode = ClipboardPolicyMode.TimeoutOnly,
        ClipboardClearSeconds = 15,
        ExportMode = ExportPolicyMode.NoPlaintext,
        PortableModeAllowed = false,
        MandatoryParanoiaMode = true,
        MandatoryWindowsHello = true,
        TotpEnabled = true
    };
}

public static class PolicyEnforcer
{
    public static Argon2Parameters EnforceKdfMinimum(Argon2Parameters requested, FortivaPolicy policy)
    {
        var min = policy.MinimumKdf;
        return new Argon2Parameters(
            Math.Max(requested.MemoryKb, min.MemoryKb),
            Math.Max(requested.Iterations, min.Iterations),
            Math.Max(requested.Parallelism, min.Parallelism),
            requested.SaltSizeBytes,
            requested.OutputSizeBytes);
    }

    public static int EnforceAutoLock(int requestedSeconds, FortivaPolicy policy)
        => Math.Min(requestedSeconds, policy.MaxAutoLockSeconds);

    public static bool CanUsePortableMode(FortivaPolicy? policy)
        => policy?.PortableModeAllowed ?? true;

    public static bool CanExportPlaintext(FortivaPolicy? policy)
    {
        if (policy is null) return true; // Personal with warning UI
        return policy.ExportMode == ExportPolicyMode.PlaintextWithWarning;
    }

    public static bool IsClipboardAllowed(FortivaPolicy? policy)
    {
        if (policy is null) return true;
        return !policy.ClipboardDisabled && policy.ClipboardMode != ClipboardPolicyMode.Disabled;
    }

    public static int GetClipboardClearSeconds(FortivaPolicy? policy, int personalDefault = 30)
    {
        if (policy is null) return personalDefault;
        if (policy.ClipboardDisabled) return 0;
        return policy.ClipboardClearSeconds;
    }

    /// <summary>TOTP authenticator codes — enabled for Enterprise by default, off for Personal.</summary>
    public static bool CanUseTotp(bool isEnterpriseClient, FortivaPolicy? policy)
    {
        if (policy?.TotpEnabled == false)
            return false;
        return isEnterpriseClient || (policy?.TotpEnabled ?? false);
    }

    public static SecurityLevel EnforceMinimumSecurityLevel(SecurityLevel requested, FortivaPolicy? policy)
    {
        if (policy?.MandatoryParanoiaMode == true && requested < SecurityLevel.Paranoia)
            return SecurityLevel.Paranoia;
        return requested;
    }

    public static void EnsureWritableSecurityLevel(SecurityLevel level, FortivaPolicy? policy)
    {
        if (policy?.MandatoryParanoiaMode == true && level < SecurityLevel.Paranoia)
            throw new InvalidOperationException("Paranoia Mode is required by policy.");
    }
}

public sealed class PolicyStore
{
    public const string DefaultPath = @"%PROGRAMDATA%\Fortiva\policies.json";

    public static string ExpandPath(string? path = null)
        => Environment.ExpandEnvironmentVariables(path ?? DefaultPath);

    public static FortivaPolicy Load(string? path = null, bool enterpriseDefaultsWhenMissing = false)
    {
        var p = ExpandPath(path);
        FortivaPolicy policy;
        if (!File.Exists(p))
        {
            policy = enterpriseDefaultsWhenMissing
                ? FortivaPolicy.StrictEnterprise
                : FortivaPolicy.PersonalDefault;
        }
        else
        {
            var protectedBytes = File.ReadAllBytes(p);
            var json = System.Security.Cryptography.ProtectedData.Unprotect(
                protectedBytes,
                "Fortiva.Policy.v1"u8.ToArray(),
                System.Security.Cryptography.DataProtectionScope.LocalMachine);
            policy = System.Text.Json.JsonSerializer.Deserialize<FortivaPolicy>(json)
                ?? FortivaPolicy.StrictEnterprise;
        }

        if (enterpriseDefaultsWhenMissing)
            ApplyRegistryOverrides(policy);

        return policy;
    }

    /// <summary>
    /// HKLM overrides documented in packaging/intune/README.md — takes precedence over policies.json.
    /// </summary>
    internal static void ApplyRegistryOverrides(FortivaPolicy policy)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Fortiva\Enterprise\Policy");
            if (key is null) return;

            if (key.GetValue("MaxAutoLockSeconds") is int maxLock)
                policy.MaxAutoLockSeconds = maxLock;
            if (key.GetValue("ClipboardClearSeconds") is int clipSec)
                policy.ClipboardClearSeconds = clipSec;
            if (key.GetValue("MinArgon2MemoryKb") is int memKb)
                policy.MinArgon2MemoryKb = memKb;
            if (key.GetValue("ExportMode") is int exportMode && Enum.IsDefined(typeof(ExportPolicyMode), exportMode))
                policy.ExportMode = (ExportPolicyMode)exportMode;
            if (key.GetValue("PortableModeAllowed") is int portable)
                policy.PortableModeAllowed = portable != 0;
            if (key.GetValue("MandatoryWindowsHello") is int hello)
                policy.MandatoryWindowsHello = hello != 0;
            if (key.GetValue("MandatoryParanoiaMode") is int paranoia)
                policy.MandatoryParanoiaMode = paranoia != 0;
            if (key.GetValue("ClipboardDisabled") is int clipOff)
                policy.ClipboardDisabled = clipOff != 0;
        }
        catch
        {
            // Non-admin or policy key absent — ignore.
        }
    }

    public static void Save(FortivaPolicy policy, string? path = null)
    {
        var p = ExpandPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(policy);
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            json,
            "Fortiva.Policy.v1"u8.ToArray(),
            System.Security.Cryptography.DataProtectionScope.LocalMachine);
        var temp = p + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, p, overwrite: true);
    }
}
