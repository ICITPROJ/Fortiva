namespace Fortiva.Core.Platform;

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

    public static string GetHelloDataDirectory(bool enterprise) =>
        enterprise
            ? Path.Combine(EnterpriseCrashLogDirectory, "Hello")
            : PersonalVaultDirectory;

    public static string GetBridgeSessionDirectory(bool enterprise) =>
        enterprise ? EnterpriseCrashLogDirectory : PersonalCrashLogDirectory;

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

    /// <summary>Delete all Personal user data (same paths as uninstall). Used by QA scripts only.</summary>
    public static void DeletePersonalUserData()
    {
        foreach (var dir in PersonalUninstallDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch { /* caller may retry */ }
        }
    }
}
