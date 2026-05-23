using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

/// <summary>Persisted personal-edition preferences (non-secret). Stored beside vault metadata.</summary>
public sealed class PersonalUserSettings
{
    public bool ParanoiaMode { get; set; }
    public int AutoLockSeconds { get; set; } = 300;
    public int ClipboardClearSeconds { get; set; } = 30;
    /// <summary>Check icmclab release feed and apply verified updates (Personal only).</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    /// <summary>Last portable vault directory (USB). Null when using the local profile vault.</summary>
    public string? PortableVaultDirectory { get; set; }

    private static string SettingsPath =>
        Path.Combine(FortivaPaths.PersonalDataRoot, "user.prefs.json");

    public static PersonalUserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new PersonalUserSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<PersonalUserSettings>(json) ?? new PersonalUserSettings();
        }
        catch
        {
            return new PersonalUserSettings();
        }
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
}
