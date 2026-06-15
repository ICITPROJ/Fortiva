namespace Fortiva.Core.Vault;

/// <summary>Best-effort background read of the vault file so unlock skips disk I/O on the hot path.</summary>
public sealed class VaultFilePrefetch
{
    private readonly object _gate = new();
    private string? _path;
    private byte[]? _bytes;
    private long _length;
    private DateTime _writeUtc;

    public void Begin(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return;

        lock (_gate)
        {
            InvalidateLocked();
            _path = vaultPath;
        }

        ThreadPool.QueueUserWorkItem(_ => LoadInBackground(vaultPath));
    }

    public byte[]? TryTake(string vaultPath)
    {
        lock (_gate)
        {
            if (_bytes is null || _path is null || !string.Equals(_path, vaultPath, StringComparison.OrdinalIgnoreCase))
                return null;

            if (!File.Exists(vaultPath))
            {
                InvalidateLocked();
                return null;
            }

            var info = new FileInfo(vaultPath);
            if (info.Length != _length || info.LastWriteTimeUtc != _writeUtc)
            {
                InvalidateLocked();
                return null;
            }

            var bytes = _bytes;
            InvalidateLocked();
            return bytes;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
            InvalidateLocked();
    }

    private void LoadInBackground(string vaultPath)
    {
        try
        {
            if (!File.Exists(vaultPath))
                return;

            var info = new FileInfo(vaultPath);
            var bytes = File.ReadAllBytes(vaultPath);
            lock (_gate)
            {
                if (!string.Equals(_path, vaultPath, StringComparison.OrdinalIgnoreCase))
                    return;
                _bytes = bytes;
                _length = info.Length;
                _writeUtc = info.LastWriteTimeUtc;
            }
        }
        catch
        {
            /* prefetch is optional */
        }
    }

    private void InvalidateLocked()
    {
        _bytes = null;
        _path = null;
        _length = 0;
        _writeUtc = default;
    }
}
