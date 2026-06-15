using System.Runtime.InteropServices;

namespace Fortiva.AppHost.Services;

internal sealed class HelloHardwareUnavailableException : InvalidOperationException
{
    public HelloHardwareUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

internal static class HelloHardwareErrors
{
    private const int TpmUnavailableHResult = unchecked((int)0x80098044);

    internal static bool IsHardwareUnavailable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is COMException com && IsHardwareUnavailableHResult(com.HResult))
                return true;

            if (current.HResult != 0 && IsHardwareUnavailableHResult(current.HResult))
                return true;
        }

        return false;
    }

    internal static string Describe(Exception ex)
    {
        if (!IsHardwareUnavailable(ex))
            return App.DescribeException(ex);

        return
            "TPM-backed Windows Hello is not available on this PC (Windows error 0x80098044). " +
            "Fortiva kept your existing software-backed Hello — you can still unlock with PIN, face, or fingerprint. " +
            "To try hardware-backed Hello later, confirm TPM is enabled in BIOS and Windows is up to date.";
    }

    private static bool IsHardwareUnavailableHResult(int hresult) =>
        hresult == TpmUnavailableHResult;
}
