using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class FortivaPathsTests
{
    [Fact]
    public void PersonalUninstallDirectories_IncludesVaultAndLegacyPaths()
    {
        var dirs = FortivaPaths.PersonalUninstallDirectories;

        Assert.Contains(FortivaPaths.PersonalDataRoot, dirs);
        Assert.Contains(FortivaPaths.PersonalLegacyDataRoot, dirs);
        Assert.Contains(FortivaPaths.PersonalCrashLogDirectory, dirs);
        Assert.Contains(FortivaPaths.PersonalLegacyLocalRoot, dirs);
    }

    [Fact]
    public void TryResolvePortableVaultDirectory_finds_vault_in_Fortiva_subfolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "fortiva-usb-" + Guid.NewGuid().ToString("N"));
        var fortivaDir = Path.Combine(root, "Fortiva");
        Directory.CreateDirectory(fortivaDir);
        File.WriteAllText(Path.Combine(fortivaDir, "vault.fva"), "test");

        try
        {
            Assert.True(FortivaPaths.TryResolvePortableVaultDirectory(root, out var dir));
            Assert.Equal(fortivaDir, dir);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolvePortableVaultDirectory_finds_vault_in_selected_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-usb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vault.fva"), "test");

        try
        {
            Assert.True(FortivaPaths.TryResolvePortableVaultDirectory(dir, out var resolved));
            Assert.Equal(dir, resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryGetPortableVaultCreateDirectory_uses_Fortiva_subfolder_on_drive_root()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var probe = Path.Combine(driveRoot.TrimEnd('\\'), "Fortiva");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe, recursive: true);
        }
        catch
        {
            return; // No permission to write at volume root on this machine.
        }

        try
        {
            Assert.True(FortivaPaths.TryGetPortableVaultCreateDirectory(driveRoot, out var dir));
            Assert.Equal(Path.Combine(driveRoot, "Fortiva"), dir);
        }
        finally
        {
            var created = Path.Combine(driveRoot.TrimEnd('\\'), "Fortiva");
            if (Directory.Exists(created))
                Directory.Delete(created, recursive: true);
        }
    }

    [Fact]
    public void TryGetPortableVaultCreateDirectory_uses_selected_folder_directly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-usb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            Assert.True(FortivaPaths.TryGetPortableVaultCreateDirectory(dir, out var resolved));
            Assert.Equal(dir, resolved);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryGetPortableVaultCreateDirectory_returns_false_when_vault_already_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortiva-usb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vault.fva"), "test");

        try
        {
            Assert.False(FortivaPaths.TryGetPortableVaultCreateDirectory(dir, out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
