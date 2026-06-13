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

    [Fact]
    public void EnsureDefaults_PersistsNormalizedCategories()
    {
        var settings = new PersonalUserSettings
        {
            VaultCategories = [" Work ", "work", "Personal"]
        };
        Assert.True(settings.EnsureDefaults());
        Assert.Equal(["Work", "Personal"], settings.VaultCategories);
    }

    [Fact]
    public void Load_PreservesExistingPreferencesWhenNewFieldsAdded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-prefs-test-" + Guid.NewGuid().ToString("N"));
        var prefs = Path.Combine(dir, "user.prefs.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(prefs,
                """{"ParanoiaMode":true,"AutoLockSeconds":420,"PortableVaultDirectory":"E:\\Fortiva"}""");

            var settings = LoadFromDirectory(dir);
            Assert.True(settings.ParanoiaMode);
            Assert.Equal(420, settings.AutoLockSeconds);
            Assert.Equal(@"E:\Fortiva", settings.PortableVaultDirectory);
            Assert.True(settings.AutoUpdateEnabled);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Load_FallsBackToLegacyWhenCanonicalCorrupt()
    {
        var canonicalRoot = Path.Combine(Path.GetTempPath(), "fortiva-prefs-canonical-" + Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(canonicalRoot, "Personal");
        var canonicalPrefs = Path.Combine(canonicalRoot, "user.prefs.json");
        var legacyPrefs = Path.Combine(legacyRoot, "user.prefs.json");
        try
        {
            Directory.CreateDirectory(canonicalRoot);
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(canonicalPrefs, "{bad json");
            File.WriteAllText(legacyPrefs, """{"ParanoiaMode":true,"AutoLockSeconds":240}""");

            var settings = LoadFromPaths(canonicalPrefs, legacyPrefs);
            Assert.True(settings.ParanoiaMode);
            Assert.Equal(240, settings.AutoLockSeconds);
        }
        finally
        {
            try { if (Directory.Exists(canonicalRoot)) Directory.Delete(canonicalRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TryBackupCorruptFile_RenamesExistingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-prefs-test-" + Guid.NewGuid().ToString("N"));
        var prefs = Path.Combine(dir, "user.prefs.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(prefs, "{not json");

            PersonalUserSettings.TryBackupCorruptFile(prefs);

            Assert.False(File.Exists(prefs));
            Assert.Single(Directory.GetFiles(dir, "user.prefs.json.corrupt-*.bak"));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static PersonalUserSettings LoadFromDirectory(string personalDataRoot)
    {
        var path = Path.Combine(personalDataRoot, "user.prefs.json");
        return LoadFromPaths(path, Path.Combine(personalDataRoot, "Personal", "user.prefs.json"));
    }

    private static PersonalUserSettings LoadFromPaths(string canonicalPath, string legacyPath)
    {
        PersonalUserSettings? settings = null;
        if (File.Exists(canonicalPath))
        {
            try
            {
                var json = File.ReadAllText(canonicalPath);
                settings = System.Text.Json.JsonSerializer.Deserialize<PersonalUserSettings>(json);
            }
            catch
            {
                PersonalUserSettings.TryBackupCorruptFile(canonicalPath);
            }
        }

        if (settings is null && File.Exists(legacyPath))
        {
            var json = File.ReadAllText(legacyPath);
            settings = System.Text.Json.JsonSerializer.Deserialize<PersonalUserSettings>(json);
        }

        settings ??= new PersonalUserSettings();
        settings.EnsureDefaults();
        return settings;
    }
}
