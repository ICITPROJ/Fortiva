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
    /// <summary>Check icmclab release feed and apply verified updates (Personal only).</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    /// <summary>Last portable vault directory (USB). Null when using the local profile vault.</summary>
    public string? PortableVaultDirectory { get; set; }
    /// <summary>User dismissed the one-time browser extension setup prompt.</summary>
    public bool BrowserExtensionSetupDismissed { get; set; }
    /// <summary>User-defined sidebar categories (tags), including empty ones.</summary>
    public List<string> VaultCategories { get; set; } = [];

    private static string SettingsPath =>
        Path.Combine(FortivaPaths.PersonalDataRoot, "user.prefs.json");

    private static string LegacySettingsPath =>
        Path.Combine(FortivaPaths.PersonalLegacyDataRoot, "user.prefs.json");

    public static PersonalUserSettings Load()
    {
        PersonalUserSettings? settings = null;
        var loadedFromLegacy = false;

        try
        {
            if (File.Exists(SettingsPath))
                settings = DeserializeFromFile(SettingsPath);
            else if (File.Exists(LegacySettingsPath))
            {
                settings = DeserializeFromFile(LegacySettingsPath);
                loadedFromLegacy = settings is not null;
            }
        }
        catch
        {
            settings = null;
        }

        settings ??= new PersonalUserSettings();

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

        VaultCategories = VaultCategories
            .Select(VaultTagHelper.NormalizeTag)
            .Where(t => t is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
    }

    private static PersonalUserSettings? DeserializeFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PersonalUserSettings>(json);
    }
}
