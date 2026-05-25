using System.Runtime.InteropServices;

namespace Fortiva.Core.Platform;

/// <summary>Best-effort Windows process hardening against injection and non-image backing.</summary>
public static class ProcessMitigation
{
    public static void EnableBestEffort()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            EnablePolicy(ProcessMitigationPolicy.ProcessExtensionPointDisable, 1);
            EnablePolicy(ProcessMitigationPolicy.ProcessSignaturePolicy, 1);
            EnablePolicy(ProcessMitigationPolicy.ProcessRedirectionTrustPolicy, 1);
        }
        catch
        {
            /* unsupported on older builds */
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
        ProcessSignaturePolicy = 8,
        ProcessRedirectionTrustPolicy = 12
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(
        ProcessMitigationPolicy policy,
        IntPtr lpBuffer,
        UIntPtr dwLength);
}
