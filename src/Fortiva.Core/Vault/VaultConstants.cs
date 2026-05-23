namespace Fortiva.Core.Vault;

public static class VaultConstants
{
    public const string Magic = "FORTIVA";
    public const byte FormatVersion = 1;
    public const byte MinSupportedVersion = 1;
    public const int SnapshotCount = 5;
    public const string VaultFileName = "vault.fva";
    public const string SnapshotPrefix = "vault.fva.snapshot";
    public const string TempSuffix = ".tmp";

    public const int MaxVaultFileBytes = 256 * 1024 * 1024;
    public const int MaxFieldBytes = 64 * 1024 * 1024;

    public static string SnapshotFileName(int index) => $"{SnapshotPrefix}{index}";
}
