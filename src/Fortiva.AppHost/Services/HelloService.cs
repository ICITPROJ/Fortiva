using Windows.Security.Credentials.UI;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Windows Hello / biometric unlock via UserConsentVerifier.
/// Only gates access to the DPAPI-protected key blob — never decrypts vault contents directly.
/// </summary>
public static class HelloService
{
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            return availability == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HelloVerificationResult> VerifyAsync(string message)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
                return new HelloVerificationResult(false, "Windows Hello is not available on this device.");

            var result = await UserConsentVerifier.RequestVerificationAsync(message);
            return result switch
            {
                UserConsentVerificationResult.Verified =>
                    new HelloVerificationResult(true, null),
                UserConsentVerificationResult.DeviceNotPresent =>
                    new HelloVerificationResult(false, "Biometric device not present."),
                UserConsentVerificationResult.Canceled =>
                    new HelloVerificationResult(false, "Verification was cancelled."),
                UserConsentVerificationResult.DisabledByPolicy =>
                    new HelloVerificationResult(false, "Windows Hello is disabled by policy."),
                _ =>
                    new HelloVerificationResult(false, "Verification failed.")
            };
        }
        catch (Exception ex)
        {
            return new HelloVerificationResult(false, ex.Message);
        }
    }
}

public sealed record HelloVerificationResult(bool Verified, string? ErrorMessage);
