using Fortiva.AppHost.Services;
using Fortiva.Core.Platform;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class PersonalUserSettingsTests
{
    [Fact]
    public void EnsureDefaults_RepairsMissingAutoLockFromJson()
    {
        var settings = new PersonalUserSettings { AutoLockSeconds = 0, ClipboardClearSeconds = 30 };
        Assert.True(settings.EnsureDefaults());
        Assert.Equal(PersonalUserSettings.DefaultAutoLockSeconds, settings.AutoLockSeconds);
    }

    [Fact]
    public void EnsureDefaults_RepairsInvalidClipboardSeconds()
    {
        var settings = new PersonalUserSettings { AutoLockSeconds = 120, ClipboardClearSeconds = 0 };
        Assert.True(settings.EnsureDefaults());
        Assert.Equal(PersonalUserSettings.DefaultClipboardClearSeconds, settings.ClipboardClearSeconds);
    }

    [Fact]
    public void EnsureDefaults_KeepsValidValues()
    {
        var settings = new PersonalUserSettings { AutoLockSeconds = 600, ClipboardClearSeconds = 45 };
        Assert.False(settings.EnsureDefaults());
        Assert.Equal(600, settings.AutoLockSeconds);
        Assert.Equal(45, settings.ClipboardClearSeconds);
    }

    [Fact]
    public void Load_RepairsZeroAutoLockFromDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-prefs-test-" + Guid.NewGuid().ToString("N"));
        var prefs = Path.Combine(dir, "user.prefs.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(prefs, """{"ClipboardClearSeconds":45}""");

            var settings = LoadFromDirectory(dir);
            Assert.Equal(PersonalUserSettings.DefaultAutoLockSeconds, settings.AutoLockSeconds);
            Assert.Equal(45, settings.ClipboardClearSeconds);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static PersonalUserSettings LoadFromDirectory(string personalDataRoot)
    {
        var path = Path.Combine(personalDataRoot, "user.prefs.json");
        var json = File.ReadAllText(path);
        var settings = System.Text.Json.JsonSerializer.Deserialize<PersonalUserSettings>(json)
            ?? new PersonalUserSettings();
        settings.EnsureDefaults();
        return settings;
    }
}
