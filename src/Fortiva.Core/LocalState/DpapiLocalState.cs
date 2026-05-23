using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Vault;

namespace Fortiva.Core.LocalState;

public enum DpapiScope
{
    CurrentUser,
    LocalMachine
}

public sealed class LocalStateMetadata
{
    public SecurityLevel MaxSecurityLevel { get; set; }
    public Guid LastVaultId { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public ulong LastRevisionCounter { get; set; }
}

public sealed class DpapiLocalStateStore
{
    private readonly string _statePath;
    private readonly DataProtectionScope _scope;

    public DpapiLocalStateStore(string stateDirectory, DpapiScope scope)
    {
        Directory.CreateDirectory(stateDirectory);
        _statePath = Path.Combine(stateDirectory, "local.state");
        _scope = scope switch
        {
            DpapiScope.CurrentUser => DataProtectionScope.CurrentUser,
            DpapiScope.LocalMachine => DataProtectionScope.LocalMachine,
            _ => DataProtectionScope.CurrentUser
        };
    }

    public LocalStateMetadata? Load()
    {
        if (!File.Exists(_statePath)) return null;
        var protectedBytes = File.ReadAllBytes(_statePath);
        var json = ProtectedData.Unprotect(protectedBytes, GetEntropy(), _scope);
        return JsonSerializer.Deserialize<LocalStateMetadata>(json);
    }

    public void Save(LocalStateMetadata metadata)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(metadata);
        var protectedBytes = ProtectedData.Protect(json, GetEntropy(), _scope);
        var temp = _statePath + VaultConstants.TempSuffix;
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _statePath, overwrite: true);
    }

    public RollbackCheckResult CheckRollback(VaultHeader header, bool paranoiaMode)
    {
        var stored = Load();
        if (stored is null)
            return RollbackCheckResult.Ok();

        var warnings = new List<string>();

        if (header.SecurityLevel < stored.MaxSecurityLevel)
            warnings.Add($"Vault security level ({header.SecurityLevel}) is below previously recorded maximum ({stored.MaxSecurityLevel}).");

        if (stored.LastVaultId != Guid.Empty && header.VaultId != stored.LastVaultId)
            warnings.Add("Vault ID differs from last known vault.");

        if (header.LastModifiedAt < stored.LastModifiedAt)
            warnings.Add("Vault last-modified timestamp is older than local state.");

        if (header.RevisionCounter < stored.LastRevisionCounter)
            warnings.Add("Vault revision counter decreased (possible rollback).");

        if (warnings.Count == 0)
            return RollbackCheckResult.Ok();

        return new RollbackCheckResult
        {
            IsSuspicious = true,
            Warnings = warnings,
            RequiresConfirmation = true,
            ForceReadOnly = paranoiaMode && header.SecurityLevel < stored.MaxSecurityLevel
        };
    }

    public void UpdateFromHeader(VaultHeader header)
    {
        var stored = Load() ?? new LocalStateMetadata();
        stored.MaxSecurityLevel = (SecurityLevel)Math.Max((byte)stored.MaxSecurityLevel, (byte)header.SecurityLevel);
        stored.LastVaultId = header.VaultId;
        stored.LastModifiedAt = header.LastModifiedAt;
        stored.LastRevisionCounter = Math.Max(stored.LastRevisionCounter, header.RevisionCounter);
        Save(stored);
    }

    private static byte[] GetEntropy() => "Fortiva.LocalState.v1"u8.ToArray();
}

public sealed class RollbackCheckResult
{
    public bool IsSuspicious { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool RequiresConfirmation { get; init; }
    public bool ForceReadOnly { get; init; }

    public static RollbackCheckResult Ok() => new();
}
