using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Platform;
using Fortiva.Core.Vault;

namespace Fortiva.Core.Updates;

/// <summary>
/// Best-effort copy of vault metadata before Personal auto-update installs.
/// Stored under LocalAppData so a bad installer cannot remove the only recovery copy.
/// </summary>
public static class PreUpdateVaultBackup
{
    public const int MaxRetainedBackups = 3;

    public static string BackupRoot =>
        Path.Combine(FortivaPaths.PersonalCrashLogDirectory, "pre-update-backups");

    private static readonly string[] SidecarFileNames =
    [
        "hello.keyprotect",
        "hello.binding"
    ];

    public sealed class Result
    {
        public bool VaultCopied { get; init; }
        public string? BackupDirectory { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>Copies encrypted vault + sidecars. No-op when vault.fva is missing.</summary>
    public static Result TryCreate(string vaultDirectory, string targetAppVersion)
    {
        if (string.IsNullOrWhiteSpace(vaultDirectory))
            return new Result { ErrorMessage = "Vault directory is required." };

        try
        {
            var vaultFile = Path.Combine(vaultDirectory, VaultConstants.VaultFileName);
            if (!File.Exists(vaultFile))
                return new Result();

            Directory.CreateDirectory(BackupRoot);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var dirTag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vaultDirectory)))
                .ToLowerInvariant()[..8];
            var versionTag = SanitizeForPathSegment(targetAppVersion);
            var backupDir = Path.Combine(BackupRoot, $"{stamp}-v{versionTag}-{dirTag}");
            Directory.CreateDirectory(backupDir);

            var copied = new List<string>();
            CopyFile(vaultFile, Path.Combine(backupDir, VaultConstants.VaultFileName), copied);

            foreach (var name in SidecarFileNames)
                CopyIfExists(Path.Combine(vaultDirectory, name), Path.Combine(backupDir, name), copied);

            var prefs = Path.Combine(FortivaPaths.PersonalDataRoot, "user.prefs.json");
            CopyIfExists(prefs, Path.Combine(backupDir, "user.prefs.json"), copied);

            WriteManifest(backupDir, vaultDirectory, targetAppVersion, copied);
            PruneOldBackups(BackupRoot);

            return new Result { VaultCopied = true, BackupDirectory = backupDir };
        }
        catch (Exception ex)
        {
            return new Result { ErrorMessage = ex.Message };
        }
    }

    internal static void PruneOldBackups(string backupRoot, int keep = MaxRetainedBackups)
    {
        if (!Directory.Exists(backupRoot))
            return;

        var folders = Directory.GetDirectories(backupRoot)
            .OrderByDescending(static d => d, StringComparer.OrdinalIgnoreCase)
            .Skip(keep)
            .ToList();

        foreach (var folder in folders)
        {
            try { Directory.Delete(folder, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void WriteManifest(
        string backupDir,
        string sourceVaultDirectory,
        string targetAppVersion,
        IReadOnlyList<string> copiedFileNames)
    {
        var manifest = new BackupManifest
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            TargetAppVersion = targetAppVersion,
            SourceVaultDirectory = sourceVaultDirectory,
            Files = copiedFileNames.ToList()
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(backupDir, "manifest.json"), json);
    }

    private static void CopyIfExists(string source, string dest, ICollection<string> copied)
    {
        if (!File.Exists(source))
            return;
        CopyFile(source, dest, copied);
    }

    private static void CopyFile(string source, string dest, ICollection<string> copied)
    {
        File.Copy(source, dest, overwrite: true);
        copied.Add(Path.GetFileName(dest));
    }

    private static string SanitizeForPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
            sb.Append(invalid.Contains(ch) ? '-' : ch);
        return sb.ToString();
    }

    private sealed class BackupManifest
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public string TargetAppVersion { get; init; } = "";
        public string SourceVaultDirectory { get; init; } = "";
        public List<string> Files { get; init; } = [];
    }
}
