using System.Text.Json.Serialization;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Vault;

public enum SecurityLevel : byte
{
    Standard = 0,
    Enhanced = 1,
    Paranoia = 2
}

public sealed class VaultHeader
{
    public byte FormatVersion { get; set; } = VaultConstants.FormatVersion;
    public byte MinSupportedVersion { get; set; } = VaultConstants.MinSupportedVersion;
    public Argon2Parameters KdfParameters { get; set; } = Argon2Parameters.PersonalDefault;
    public SecurityLevel SecurityLevel { get; set; } = SecurityLevel.Standard;
    public Guid VaultId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public ulong RevisionCounter { get; set; }
    public ulong SecurityLevelCounter { get; set; }
    public byte[] Salt { get; set; } = [];
    public byte[] WrappedVaultKey { get; set; } = [];
    public byte[] HeaderMac { get; set; } = [];

    public byte[] SerializeForMac()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(FormatVersion);
        bw.Write(MinSupportedVersion);
        var kdfBytes = KdfParameters.ToBytes();
        bw.Write(kdfBytes.Length);
        bw.Write(kdfBytes);
        bw.Write((byte)SecurityLevel);
        bw.Write(VaultId.ToByteArray());
        bw.Write(CreatedAt.UtcTicks);
        bw.Write(LastModifiedAt.UtcTicks);
        bw.Write(RevisionCounter);
        bw.Write(SecurityLevelCounter);
        bw.Write(Salt.Length);
        bw.Write(Salt);
        bw.Write(WrappedVaultKey.Length);
        bw.Write(WrappedVaultKey);
        return ms.ToArray();
    }
}

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PasswordLastChanged { get; set; }
    public bool IsSecureNote { get; set; }
    public bool IsFavorite { get; set; }
    public string? PasskeyCredentialId { get; set; }
    public string? PasskeyRpId { get; set; }
    public string? TotpSecret { get; set; }
    public string? CustomFields { get; set; }

    public VaultEntry Clone() => new()
    {
        Id = Id,
        Title = Title,
        Username = Username,
        Password = Password,
        Url = Url,
        Notes = Notes,
        Tags = new List<string>(Tags),
        CreatedAt = CreatedAt,
        ModifiedAt = ModifiedAt,
        PasswordLastChanged = PasswordLastChanged,
        IsSecureNote = IsSecureNote,
        IsFavorite = IsFavorite,
        PasskeyCredentialId = PasskeyCredentialId,
        PasskeyRpId = PasskeyRpId,
        TotpSecret = TotpSecret,
        CustomFields = CustomFields
    };
}

public sealed class IntegrityLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = "";
    public Guid? EntryId { get; set; }
    public ulong RevisionAfter { get; set; }
    public byte[] EntryHash { get; set; } = [];
}

public sealed class VaultPayload
{
    public List<VaultEntry> Entries { get; set; } = [];
    public List<IntegrityLogEntry> IntegrityLog { get; set; } = [];
}

public sealed class VaultUnlockContext
{
    public VaultHeader Header { get; init; } = null!;
    public VaultPayload Payload { get; init; } = null!;
    public KeyHierarchy Keys { get; set; } = null!;
    public bool ReadOnly { get; init; }
    public string? RollbackWarning { get; init; }
}

public enum RollbackAction
{
    Warn,
    RequireConfirmation,
    ReadOnlyInParanoia
}
