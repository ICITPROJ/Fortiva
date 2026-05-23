namespace Fortiva.Core.Vault;

public sealed class VaultSnapshotManager
{
    private readonly string _directory;
    private readonly int _snapshotCount;

    public VaultSnapshotManager(string vaultDirectory, int snapshotCount = VaultConstants.SnapshotCount)
    {
        _directory = vaultDirectory;
        _snapshotCount = snapshotCount;
    }

    public string VaultPath => Path.Combine(_directory, VaultConstants.VaultFileName);

    public void RotateSnapshotAfterSave()
    {
        if (!File.Exists(VaultPath)) return;

        // Drop oldest
        var oldest = Path.Combine(_directory, VaultConstants.SnapshotFileName(_snapshotCount));
        if (File.Exists(oldest))
            File.Delete(oldest);

        // Shift snapshots N-1..1 → N..2
        for (var i = _snapshotCount - 1; i >= 1; i--)
        {
            var src = Path.Combine(_directory, VaultConstants.SnapshotFileName(i));
            var dst = Path.Combine(_directory, VaultConstants.SnapshotFileName(i + 1));
            if (File.Exists(src))
                File.Move(src, dst, overwrite: true);
        }

        var first = Path.Combine(_directory, VaultConstants.SnapshotFileName(1));
        File.Copy(VaultPath, first, overwrite: true);
    }

    public string? FindLatestSnapshot()
    {
        for (var i = 1; i <= _snapshotCount; i++)
        {
            var path = Path.Combine(_directory, VaultConstants.SnapshotFileName(i));
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public IReadOnlyList<string> ListSnapshots()
    {
        var list = new List<string>();
        for (var i = 1; i <= _snapshotCount; i++)
        {
            var path = Path.Combine(_directory, VaultConstants.SnapshotFileName(i));
            if (File.Exists(path))
                list.Add(path);
        }
        return list;
    }
}
