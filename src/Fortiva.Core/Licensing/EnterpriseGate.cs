namespace Fortiva.Core.Licensing;

/// <summary>Centralized Enterprise edition license enforcement for service-layer operations.</summary>
public static class EnterpriseGate
{
    public static void RequireValidLicense(bool isEnterprise, bool isAdmin, bool isLicenseValid)
    {
        if (isEnterprise && !isAdmin && !isLicenseValid)
            throw new InvalidOperationException("A valid enterprise license is required.");
    }
}
