using System.Runtime.InteropServices;
using Fortiva.Core.Hello;
using Windows.Security.Credentials.UI;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Windows Hello unlock (face, fingerprint, or PIN) via UserConsentVerifier.
/// Unpackaged WinUI apps must parent the dialog to the app HWND via
/// <see cref="UserConsentVerifierInterop.RequestVerificationForWindowAsync"/>.
/// </summary>
public static class HelloService
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static async Task<bool> IsAvailableAsync()
    {
        var availability = await GetAvailabilityAsync().ConfigureAwait(true);
        return availability == UserConsentVerifierAvailability.Available;
    }

    public static async Task<HelloVerificationResult> VerifyAsync(string message)
    {
        try
        {
            var availability = await GetAvailabilityAsync().ConfigureAwait(true);
            if (availability != UserConsentVerifierAvailability.Available)
                return new HelloVerificationResult(false, DescribeAvailability(availability));

            var result = await RequestVerificationAsync(message).ConfigureAwait(true);
            if (result == UserConsentVerificationResult.Verified)
                HelloVerificationGate.MarkVerified();
            return MapResult(result);
        }
        catch (Exception ex)
        {
            App.LogException("HelloService.VerifyAsync", ex);
            return new HelloVerificationResult(false, UserFacingError(ex));
        }
    }

    private static async Task<UserConsentVerifierAvailability> GetAvailabilityAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync().AsTask().ConfigureAwait(true);
        }
        catch
        {
            return UserConsentVerifierAvailability.DeviceNotPresent;
        }
    }

    private static async Task<UserConsentVerificationResult> RequestVerificationAsync(string message)
    {
        var hwnd = App.EnsureMainWindowHandle();
        PrepareOwnerWindow(hwnd);

        if (hwnd != IntPtr.Zero)
        {
            try
            {
                return await UserConsentVerifierInterop
                    .RequestVerificationForWindowAsync(hwnd, message)
                    .AsTask()
                    .ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is InvalidCastException or COMException or UnauthorizedAccessException)
            {
                App.LogException("HelloService.RequestVerificationForWindow", ex);
                return await UserConsentVerifier.RequestVerificationAsync(message)
                    .AsTask()
                    .ConfigureAwait(true);
            }
        }

        App.LogException("HelloService.RequestVerificationAsync",
            new InvalidOperationException("Fortiva main window handle was not available for Windows Hello."));

        // HWND missing only — single non-windowed attempt (never chain two dialogs).
        return await UserConsentVerifier.RequestVerificationAsync(message)
            .AsTask()
            .ConfigureAwait(true);
    }

    private static void PrepareOwnerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
        catch
        {
            /* best effort — verification may still succeed */
        }
    }

    internal static string DescribeAvailability(UserConsentVerifierAvailability availability) =>
        availability switch
        {
            UserConsentVerifierAvailability.NotConfiguredForUser =>
                "Windows Hello is not set up on this PC. Open Settings → Accounts → Sign-in options and add face recognition, fingerprint, or PIN.",
            UserConsentVerifierAvailability.DeviceNotPresent =>
                "No Windows Hello sign-in method is available. Set up face, fingerprint, or a PIN in Windows Settings.",
            UserConsentVerifierAvailability.DisabledByPolicy =>
                "Windows Hello is disabled by your organization.",
            UserConsentVerifierAvailability.DeviceBusy =>
                "Windows Hello is busy. Wait a moment and try again.",
            _ => "Windows Hello is not available on this device."
        };

    internal static HelloVerificationResult MapResult(UserConsentVerificationResult result) =>
        result switch
        {
            UserConsentVerificationResult.Verified =>
                new HelloVerificationResult(true, null),
            UserConsentVerificationResult.Canceled =>
                new HelloVerificationResult(false, "Verification was cancelled."),
            UserConsentVerificationResult.DeviceNotPresent =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DeviceNotPresent)),
            UserConsentVerificationResult.DisabledByPolicy =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DisabledByPolicy)),
            UserConsentVerificationResult.NotConfiguredForUser =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.NotConfiguredForUser)),
            UserConsentVerificationResult.DeviceBusy =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DeviceBusy)),
            UserConsentVerificationResult.RetriesExhausted =>
                new HelloVerificationResult(false, "Too many failed attempts. Try again later or use your master password."),
            _ =>
                new HelloVerificationResult(false, "Windows Hello verification failed. Try again or use your master password.")
        };

    private static string UserFacingError(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message)
            ? "Windows Hello verification failed."
            : ex.Message;
}

public sealed record HelloVerificationResult(bool Verified, string? ErrorMessage);
