using System.Security.Cryptography;
using System.Text;

namespace Fortiva.Core.Vault;

/// <summary>
/// Cross-process mutex keyed by vault path so only one save/read-check runs at a time.
/// Avoids holding an exclusive file handle while re-reading the vault for revision checks.
/// </summary>
internal static class VaultSaveMutex
{
    private const int WaitMs = 30_000;

    public static IDisposable Acquire(string vaultPath)
    {
        var name = BuildMutexName(vaultPath);
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
            if (!mutex.WaitOne(WaitMs))
            {
                throw new VaultConcurrencyException(
                    "Timed out waiting for the vault save lock. Close other Fortiva windows and try again.");
            }

            return new Releaser(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new Releaser(mutex!);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    private static string BuildMutexName(string vaultPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(vaultPath)));
        return @"Local\Fortiva.VaultSave." + Convert.ToHexString(hash)[..24];
    }

    private sealed class Releaser(Mutex mutex) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { mutex.ReleaseMutex(); } catch { /* abandoned */ }
            mutex.Dispose();
        }
    }
}
