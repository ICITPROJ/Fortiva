using System.Runtime.InteropServices;

namespace Fortiva.Core.Platform;

/// <summary>Best-effort Windows process hardening against injection and non-image backing.</summary>
public static class ProcessMitigation
{
    public static void EnableBestEffort()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Each policy is isolated so one unsupported/failed call can't skip the rest.
        TryEnablePolicy(ProcessMitigationPolicy.ProcessExtensionPointDisable, 1);
        TryEnablePolicy(ProcessMitigationPolicy.ProcessRedirectionTrustPolicy, 1);

        // NOTE: ProcessSignaturePolicy (MicrosoftSignedOnly / StoreSignedOnly) is intentionally
        // NOT enabled. It forces every later-loaded module to carry a Microsoft/Store signature,
        // which our own (Authenticode- or un-signed) dependencies can never satisfy. Enabling it
        // blocks lazily-loaded assemblies such as Isopoh.Cryptography.Argon2 during vault
        // create/unlock with ERROR_INVALID_IMAGE_HASH (0x80070241). Do not re-add unless Fortiva
        // ships as a Microsoft Store package whose entire dependency closure is store-signed.
    }

    private static void TryEnablePolicy(ProcessMitigationPolicy policy, uint flags)
    {
        try
        {
            EnablePolicy(policy, flags);
        }
        catch
        {
            /* unsupported on older builds / best-effort hardening */
        }
    }

    private static void EnablePolicy(ProcessMitigationPolicy policy, uint flags)
    {
        var size = Marshal.SizeOf<uint>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(ptr, (int)flags);
            if (!SetProcessMitigationPolicy(policy, ptr, (UIntPtr)size))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private enum ProcessMitigationPolicy
    {
        ProcessExtensionPointDisable = 6,
        ProcessRedirectionTrustPolicy = 12
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(
        ProcessMitigationPolicy policy,
        IntPtr lpBuffer,
        UIntPtr dwLength);
}
