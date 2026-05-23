namespace Fortiva.Core.Passkeys;

/// <summary>
/// Hooks for Windows WebAuthn/FIDO2 passkey credential IDs stored alongside vault entries.
/// Full WebAuthn ceremony is hosted in WinUI; Core stores opaque credential IDs only.
/// </summary>
public sealed class PasskeyCredentialRef
{
    public string CredentialId { get; set; } = "";
    public string RpId { get; set; } = "";
    public string UserHandle { get; set; } = "";
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PasskeyStorage
{
    public static bool IsWebAuthnAvailable()
    {
        try
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
        }
        catch
        {
            return false;
        }
    }
}
