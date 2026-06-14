namespace Fortiva.Core.Platform;

using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>Canonical on-disk locations for Fortiva data (must match installer uninstall scripts).</summary>
public static class FortivaPaths
{
    public static string PersonalDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fortiva");

    /// <summary>Legacy incorrect path used by early installers — still cleaned on uninstall.</summary>
    public static string PersonalLegacyDataRoot =>
        Path.Combine(PersonalDataRoot, "Personal");

    public static string PersonalVaultDirectory => PersonalDataRoot;

    public static string PersonalCrashLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortivaPersonal");

    public static string PersonalAuditDirectory =>
        Path.Combine(PersonalCrashLogDirectory, "audit");

    /// <summary>Legacy/alternate local folder — cleaned on uninstall.</summary>
    public static string PersonalLegacyLocalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fortiva");

    public static string PersonalVaultPath =>
        Path.Combine(PersonalVaultDirectory, Vault.VaultConstants.VaultFileName);

    public static string GetHelloDataDirectory(bool enterprise, string? vaultDirectory = null) =>
        enterprise
            ? Path.Combine(EnterpriseCrashLogDirectory, "Hello")
            : vaultDirectory ?? PersonalVaultDirectory;

    public static string GetBridgeSessionDirectory(bool enterprise) =>
        enterprise ? EnterpriseCrashLogDirectory : PersonalCrashLogDirectory;

    /// <summary>Per-user rollback state directory keyed by vault location (enterprise shared vaults).</summary>
    public static string GetRollbackStateDirectory(string vaultDirectory, bool enterprise)
    {
        if (!enterprise)
            return vaultDirectory;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vaultDirectory)))
            .ToLowerInvariant()[..16];
        return Path.Combine(EnterpriseCrashLogDirectory, "rollback", hash);
    }

    /// <summary>
    /// Moves vault from legacy %APPDATA%\Fortiva\Personal\ to %APPDATA%\Fortiva\ when needed.
    /// </summary>
    public static bool MigrateLegacyPersonalVaultIfNeeded()
    {
        var legacyVault = Path.Combine(PersonalLegacyDataRoot, Vault.VaultConstants.VaultFileName);
        var canonicalVault = PersonalVaultPath;
        if (!File.Exists(legacyVault) || File.Exists(canonicalVault))
            return false;

        Directory.CreateDirectory(PersonalVaultDirectory);
        File.Move(legacyVault, canonicalVault);

        foreach (var name in new[] { "local.state", "hello.keyprotect", "hello.binding" })
        {
            var legacy = Path.Combine(PersonalLegacyDataRoot, name);
            var dest = Path.Combine(PersonalVaultDirectory, name);
            if (File.Exists(legacy) && !File.Exists(dest))
                File.Move(legacy, dest);
        }

        foreach (var snap in Directory.GetFiles(PersonalLegacyDataRoot, Vault.VaultConstants.VaultFileName + ".snapshot*"))
        {
            var dest = Path.Combine(PersonalVaultDirectory, Path.GetFileName(snap));
            if (!File.Exists(dest))
                File.Move(snap, dest);
        }

        return true;
    }

    public static string EnterpriseProgramData =>
        Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva");

    public static string EnterpriseAuditDirectory =>
        Path.Combine(EnterpriseProgramData, "audit");

    public static string EnterpriseHelloDirectory =>
        Path.Combine(EnterpriseCrashLogDirectory, "Hello");

    public static string EnterpriseCrashLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortivaEnterprise");

    public static string AdminCrashLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortivaAdmin");

    public static string PortableVaultDirectory(string usbRoot) =>
        Path.Combine(usbRoot, "Fortiva");

    /// <summary>
    /// Resolves a folder picked on a removable drive to the directory containing vault.fva.
    /// Accepts the drive root (…/Fortiva/vault.fva), a Fortiva folder, or any folder with vault.fva.
    /// </summary>
    public static bool TryResolvePortableVaultDirectory(string selectedPath, out string vaultDirectory)
    {
        vaultDirectory = "";
        if (string.IsNullOrWhiteSpace(selectedPath))
            return false;

        selectedPath = NormalizePickerPath(selectedPath);

        var directVault = Path.Combine(selectedPath, Vault.VaultConstants.VaultFileName);
        if (File.Exists(directVault))
        {
            vaultDirectory = selectedPath;
            return true;
        }

        var nested = Path.Combine(selectedPath, "Fortiva", Vault.VaultConstants.VaultFileName);
        if (File.Exists(nested))
        {
            vaultDirectory = Path.Combine(selectedPath, "Fortiva");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves where a new portable vault should be created when the user picks a folder without vault.fva.
    /// Drive root → …/Fortiva/; an existing Fortiva folder → that folder; otherwise the selected folder.
    /// </summary>
    public static bool TryGetPortableVaultCreateDirectory(string selectedPath, out string vaultDirectory)
    {
        vaultDirectory = "";
        if (string.IsNullOrWhiteSpace(selectedPath))
            return false;

        selectedPath = NormalizePickerPath(selectedPath);

        if (TryResolvePortableVaultDirectory(selectedPath, out vaultDirectory))
            return false;

        var root = Path.GetPathRoot(selectedPath);
        if (root is not null &&
            string.Equals(NormalizeDirectoryPath(selectedPath), NormalizeDirectoryPath(root), StringComparison.OrdinalIgnoreCase))
        {
            vaultDirectory = PortableVaultDirectory(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        else if (string.Equals(Path.GetFileName(selectedPath), "Fortiva", StringComparison.OrdinalIgnoreCase))
        {
            vaultDirectory = selectedPath;
        }
        else
        {
            vaultDirectory = selectedPath;
        }

        try
        {
            Directory.CreateDirectory(vaultDirectory);
            return !File.Exists(Path.Combine(vaultDirectory, Vault.VaultConstants.VaultFileName));
        }
        catch
        {
            vaultDirectory = "";
            return false;
        }
    }

    private static string NormalizeDirectoryPath(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Normalize a folder-picker path without turning drive roots (e.g. E:\) into cwd-relative paths.</summary>
    private static string NormalizePickerPath(string selectedPath)
    {
        selectedPath = selectedPath.Trim();
        var root = Path.GetPathRoot(selectedPath);
        if (root is not null &&
            string.Equals(NormalizeDirectoryPath(selectedPath), NormalizeDirectoryPath(root), StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(root);
        if (selectedPath.Length == 2 && selectedPath[1] == ':')
            return Path.GetFullPath(selectedPath + Path.DirectorySeparatorChar);
        return Path.GetFullPath(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public static string GetVaultDirectory(bool portable, string? portableRoot = null)
        => portable && portableRoot is not null
            ? PortableVaultDirectory(portableRoot)
            : PersonalVaultDirectory;

    public static bool PersonalVaultExists =>
        File.Exists(PersonalVaultPath);

    /// <summary>
    /// All Personal-edition user folders removed by the installer on uninstall.
    /// Keep in sync with packaging/installer/FortivaPersonal.iss and scripts/FortivaPersonalPaths.ps1.
    /// </summary>
    public static IReadOnlyList<string> PersonalUninstallDirectories =>
    [
        PersonalLegacyDataRoot,
        PersonalDataRoot,
        PersonalCrashLogDirectory,
        PersonalLegacyLocalRoot
    ];

    /// <summary>LocalAppData folders removed when Enterprise client is uninstalled.</summary>
    public static IReadOnlyList<string> EnterpriseUninstallLocalDirectories =>
        [EnterpriseCrashLogDirectory];

    /// <summary>ProgramData files removed when user opts to delete enterprise vault.</summary>
    public static IReadOnlyList<string> EnterpriseVaultFileNames =>
        [Vault.VaultConstants.VaultFileName, "local.state"];

    /// <summary>ProgramData files removed when Admin uninstall deletes configuration.</summary>
    public static IReadOnlyList<string> AdminConfigFileNames =>
        ["policies.json", "license.dat", "shared-vaults.json"];

    /// <summary>ProgramData directories removed when user opts to delete audit logs.</summary>
    public static IReadOnlyList<string> SharedAuditUninstallDirectories =>
        [EnterpriseAuditDirectory];

    /// <summary>Downloaded Personal auto-update installers in %TEMP% (cleaned on uninstall).</summary>
    public static string PersonalUpdateInstallerTempPattern =>
        "FortivaPersonal-*-Setup.exe";

    /// <summary>Known Personal vault/metadata file names under PersonalDataRoot (installer uninstall must remove these).</summary>
    public static IReadOnlyList<string> PersonalKnownDataFileNames =>
    [
        Vault.VaultConstants.VaultFileName,
        "local.state",
        "hello.keyprotect",
        "hello.binding",
        "user.prefs.json"
    ];

    /// <summary>
    /// Personal preference files under LocalAppData (never replaced by in-app or dev deploy updates).
    /// </summary>
    public static IReadOnlyList<string> PersonalLocalPreferenceFileNames =>
    [
        "appearance.json",
        "user.prefs.json"
    ];

    /// <summary>
    /// Roots that must never be overwritten by application updates or dev deploy scripts.
    /// Installers only touch <c>{localappdata}\Programs\...\Fortiva Personal\</c>.
    /// </summary>
    public static IReadOnlyList<string> PersonalUserDataRoots =>
    [
        PersonalDataRoot,
        PersonalLegacyDataRoot,
        PersonalCrashLogDirectory,
        PersonalLegacyLocalRoot
    ];

    /// <summary>True when <paramref name="path"/> is under a protected Personal user-data root.</summary>
    public static bool IsProtectedUserDataPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        foreach (var root in PersonalUserDataRoots)
        {
            if (!Directory.Exists(root) && !File.Exists(root))
            {
                try
                {
                    var normalizedRoot = Path.GetFullPath(root);
                    if (full.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(full, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    /* ignore invalid roots */
                }

                continue;
            }

            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Throws when a deploy/install target would overlap protected user data.</summary>
    public static void EnsureSafeInstallTarget(string installDirectory)
    {
        if (IsProtectedUserDataPath(installDirectory))
        {
            throw new InvalidOperationException(
                "Install target overlaps Fortiva user data. Updates must only replace files under the "
                + "application directory, never %APPDATA%\\Fortiva or %LOCALAPPDATA%\\FortivaPersonal.");
        }
    }

    /// <summary>True if a vault file exists in any canonical Personal location.</summary>
    public static bool PersonalVaultFileExists()
    {
        foreach (var path in FindPersonalVaultFilePaths())
        {
            if (File.Exists(path))
                return true;
        }
        return false;
    }

    /// <summary>All Personal vault.fva paths (canonical + legacy).</summary>
    public static IReadOnlyList<string> FindPersonalVaultFilePaths()
    {
        var paths = new List<string>();
        foreach (var dir in new[] { PersonalDataRoot, PersonalLegacyDataRoot })
        {
            var vault = Path.Combine(dir, Vault.VaultConstants.VaultFileName);
            if (File.Exists(vault))
                paths.Add(vault);
        }
        return paths;
    }

    /// <summary>Delete all Personal user data (same paths as uninstall). Used by QA scripts only.</summary>
    public static void DeletePersonalUserData(bool confirmProductionWipe)
    {
        if (!confirmProductionWipe)
        {
            throw new InvalidOperationException(
                "Refusing to delete Personal user data without confirmProductionWipe: true. "
                + "This removes vault.fva, settings, and Hello credentials.");
        }

        foreach (var dir in PersonalUninstallDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            TryDeleteDirectoryWithRetry(dir, attempts: 3);
        }
    }

    private static void TryDeleteDirectoryWithRetry(string directory, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                if (!Directory.Exists(directory))
                    return;
            }
            catch
            {
                /* file may be locked — retry after brief delay */
            }

            Thread.Sleep(750 * (attempt + 1));
        }
    }
}
