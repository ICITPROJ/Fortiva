using System.Text.Json;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

/// <summary>Per-user Enterprise client preferences (non-secret).</summary>
public sealed class EnterpriseUserSettings
{
    /// <summary>Selected vault directory. Null uses the default org vault at %PROGRAMDATA%\Fortiva.</summary>
    public string? SelectedVaultDirectory { get; set; }

    private static string SettingsPath =>
        Path.Combine(FortivaPaths.EnterpriseCrashLogDirectory, "user.prefs.json");

    public static EnterpriseUserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new EnterpriseUserSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<EnterpriseUserSettings>(json) ?? new EnterpriseUserSettings();
        }
        catch
        {
            return new EnterpriseUserSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(FortivaPaths.EnterpriseCrashLogDirectory);
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
