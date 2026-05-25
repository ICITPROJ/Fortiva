using Fortiva.Core.Updates;

namespace Fortiva.Core.Tests.Updates;

public sealed class PreUpdateVaultBackupTests
{
    [Fact]
    public void TryCreate_CopiesVaultAndSidecars()
    {
        var vaultDir = Path.Combine(Path.GetTempPath(), "fortiva-preupdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vaultDir);
        try
        {
            File.WriteAllText(Path.Combine(vaultDir, Fortiva.Core.Vault.VaultConstants.VaultFileName), "encrypted-vault-bytes");
            File.WriteAllText(Path.Combine(vaultDir, "local.state"), "state");
            File.WriteAllText(Path.Combine(vaultDir, "hello.keyprotect"), "hello");

            var result = PreUpdateVaultBackup.TryCreate(vaultDir, "1.2.3");

            Assert.True(result.VaultCopied);
            Assert.NotNull(result.BackupDirectory);
            Assert.True(Directory.Exists(result.BackupDirectory));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, Fortiva.Core.Vault.VaultConstants.VaultFileName)));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "hello.keyprotect")));
            Assert.False(File.Exists(Path.Combine(result.BackupDirectory, "local.state")));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(vaultDir))
                Directory.Delete(vaultDir, recursive: true);
            if (Directory.Exists(PreUpdateVaultBackup.BackupRoot))
            {
                try { Directory.Delete(PreUpdateVaultBackup.BackupRoot, recursive: true); }
                catch { /* temp */ }
            }
        }
    }

    [Fact]
    public void TryCreate_NoVaultFile_IsNoOp()
    {
        var vaultDir = Path.Combine(Path.GetTempPath(), "fortiva-preupdate-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vaultDir);
        try
        {
            var result = PreUpdateVaultBackup.TryCreate(vaultDir, "1.0.0");
            Assert.False(result.VaultCopied);
            Assert.Null(result.BackupDirectory);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(vaultDir))
                Directory.Delete(vaultDir, recursive: true);
        }
    }

    [Fact]
    public void PruneOldBackups_KeepsNewestOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "fortiva-preupdate-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "20260101-120000-v1-aabbccdd"));
            Directory.CreateDirectory(Path.Combine(root, "20260102-120000-v1-aabbccdd"));
            Directory.CreateDirectory(Path.Combine(root, "20260103-120000-v1-aabbccdd"));
            Directory.CreateDirectory(Path.Combine(root, "20260104-120000-v1-aabbccdd"));

            PreUpdateVaultBackup.PruneOldBackups(root, keep: 3);

            Assert.Equal(3, Directory.GetDirectories(root).Length);
            Assert.DoesNotContain(Directory.GetDirectories(root),
                d => d.EndsWith("20260101-120000-v1-aabbccdd", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
