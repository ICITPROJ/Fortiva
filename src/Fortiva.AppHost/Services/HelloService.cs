using Windows.Security.Credentials.UI;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Windows Hello unlock (face, fingerprint, or PIN) via UserConsentVerifier.
/// Unpackaged WinUI apps must use RequestVerificationForWindowAsync so Windows
/// offers the full Hello sign-in stack, not PIN-only fallback.
/// </summary>
public static class HelloService
{
    public static async Task<bool> IsAvailableAsync()
    {
        var availability = await GetAvailabilityAsync();
        return availability == UserConsentVerifierAvailability.Available;
    }

    public static async Task<HelloVerificationResult> VerifyAsync(string message)
    {
        try
        {
            var availability = await GetAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
                return new HelloVerificationResult(false, DescribeAvailability(availability));

            var result = await RequestVerificationAsync(message);
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
            return await UserConsentVerifier.CheckAvailabilityAsync();
        }
        catch
        {
            return UserConsentVerifierAvailability.DeviceNotPresent;
        }
    }

    private static async Task<UserConsentVerificationResult> RequestVerificationAsync(string message)
    {
        var hwnd = App.MainWindowHandle;
        if (hwnd != IntPtr.Zero)
            return await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message);

        return await UserConsentVerifier.RequestVerificationAsync(message);
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
