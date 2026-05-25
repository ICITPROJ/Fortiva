using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

/// <summary>Per-edition UI preferences stored under LocalApplicationData (not the vault).</summary>
public sealed class AppearanceSettings
{
    public AppThemePreference Theme { get; set; } = AppThemePreference.Dark;

    public static AppearanceSettings Load()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return new AppearanceSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppearanceSettings>(json) ?? new AppearanceSettings();
        }
        catch
        {
            return new AppearanceSettings();
        }
    }

    public void Save()
    {
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(path))
            File.Replace(temp, path, null);
        else
            File.Move(temp, path);
    }

    private static string GetPath()
    {
        var folder = App.Edition switch
        {
            "Enterprise" => FortivaPaths.EnterpriseCrashLogDirectory,
            "Admin" => FortivaPaths.AdminCrashLogDirectory,
            _ => FortivaPaths.PersonalCrashLogDirectory
        };
        return Path.Combine(folder, "appearance.json");
    }
}
