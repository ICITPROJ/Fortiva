namespace Fortiva.Core.Vault;

/// <summary>
/// Thrown when a save detects that the on-disk vault was modified by another process/instance
/// since it was unlocked (optimistic concurrency check). Prevents one writer from silently
/// clobbering another's changes (e.g. two app instances, or the same USB vault edited on two PCs).
/// </summary>
public sealed class VaultConcurrencyException : Exception
{
    public VaultConcurrencyException()
        : base("The vault was changed by another program or device since it was opened. "
            + "Your latest change was not saved. Lock and unlock the vault to load the newest "
            + "version, then re-apply your change.")
    {
    }

    public VaultConcurrencyException(string message) : base(message) { }
    public VaultConcurrencyException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a two-way sync persisted one vault but failed to persist the other, leaving them
/// temporarily divergent. Re-running the sync re-converges them (the merge is idempotent).
/// </summary>
public sealed class VaultSyncPartialException : Exception
{
    public VaultSyncPartialException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when sync could not roll both vaults back after a partial write — disks may differ.
/// User must run sync again or restore from snapshot; see <see cref="VaultSyncMarker"/>.
/// </summary>
public sealed class VaultSyncDivergedException : Exception
{
    public VaultSyncDivergedException(string message) : base(message) { }

    public VaultSyncDivergedException(string message, Exception inner) : base(message, inner) { }
}
