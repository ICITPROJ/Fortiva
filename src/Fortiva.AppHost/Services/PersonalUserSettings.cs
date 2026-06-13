using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

/// <summary>Persisted personal-edition preferences (non-secret). Stored beside vault metadata.</summary>
public sealed class PersonalUserSettings
{
    public const int DefaultAutoLockSeconds = 300;
    public const int DefaultClipboardClearSeconds = 30;
    public const int MinAutoLockSeconds = 30;
    public const int MaxAutoLockSeconds = 900;
    public const int MinClipboardClearSeconds = 5;
    public const int MaxClipboardClearSeconds = 120;

    public bool ParanoiaMode { get; set; }
    public int AutoLockSeconds { get; set; } = DefaultAutoLockSeconds;
    public int ClipboardClearSeconds { get; set; } = DefaultClipboardClearSeconds;
    /// <summary>Check GitHub Releases manifest and apply verified updates (Personal only).</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public DateTimeOffset? LastUpdateApplyFailedUtc { get; set; }
    public string? LastUpdateApplyError { get; set; }
    /// <summary>User dismissed the Hello v4 hardware upgrade prompt.</summary>
    public bool HelloHardwareUpgradeDismissed { get; set; }
    /// <summary>Last portable vault directory (USB). Null when using the local profile vault.</summary>
    public string? PortableVaultDirectory { get; set; }
    /// <summary>User dismissed the one-time browser extension setup prompt.</summary>
    public bool BrowserExtensionSetupDismissed { get; set; }
    /// <summary>User-defined sidebar categories (tags), including empty ones.</summary>
    public List<string> VaultCategories { get; set; } = [];
    /// <summary>Vault uses compact list rows instead of cards.</summary>
    public bool VaultUseListView { get; set; }

    private static string SettingsPath =>
        Path.Combine(FortivaPaths.PersonalDataRoot, "user.prefs.json");

    private static string LegacySettingsPath =>
        Path.Combine(FortivaPaths.PersonalLegacyDataRoot, "user.prefs.json");

    public static PersonalUserSettings Load()
    {
        PersonalUserSettings? settings = null;
        var loadedFromLegacy = false;

        if (File.Exists(SettingsPath))
            settings = TryDeserializeFromFile(SettingsPath);

        if (settings is null && File.Exists(LegacySettingsPath))
        {
            settings = TryDeserializeFromFile(LegacySettingsPath);
            loadedFromLegacy = settings is not null;
        }

        if (settings is null)
        {
            // Corrupt prefs were moved to *.corrupt-*.bak — do not write fresh defaults over user data.
            return new PersonalUserSettings();
        }

        var changed = settings.EnsureDefaults();
        if (loadedFromLegacy || changed)
        {
            try { settings.Save(); }
            catch { /* best effort — in-memory defaults still apply */ }
        }

        return settings;
    }

    internal bool EnsureDefaults()
    {
        var changed = false;

        if (AutoLockSeconds is < MinAutoLockSeconds or > MaxAutoLockSeconds)
        {
            AutoLockSeconds = DefaultAutoLockSeconds;
            changed = true;
        }

        if (ClipboardClearSeconds is < MinClipboardClearSeconds or > MaxClipboardClearSeconds)
        {
            ClipboardClearSeconds = DefaultClipboardClearSeconds;
            changed = true;
        }

        var normalized = VaultCategories
            .Select(VaultTagHelper.NormalizeTag)
            .Where(t => t is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!CategoriesEqual(VaultCategories, normalized))
        {
            VaultCategories = normalized;
            changed = true;
        }

        return changed;
    }

    public void Save()
    {
        Directory.CreateDirectory(FortivaPaths.PersonalDataRoot);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var path = SettingsPath;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(path))
            File.Replace(temp, path, null);
        else
            File.Move(temp, path);
        // Preferences include the portable vault location and update state; restrict to the
        // current user (PersonalDataRoot is always on a fixed profile drive).
        Fortiva.Core.Hello.HelloFileSecurity.ApplyCurrentUserOnlyAcl(path);
    }

    private static PersonalUserSettings? TryDeserializeFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PersonalUserSettings>(json);
        }
        catch
        {
            TryBackupCorruptFile(path);
            return null;
        }
    }

    internal static void TryBackupCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var backup = path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            File.Move(path, backup);
        }
        catch
        {
            // Best effort — caller falls back to defaults.
        }
    }

    private static bool CategoriesEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
