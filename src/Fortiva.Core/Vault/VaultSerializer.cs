using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Crypto;

namespace Fortiva.Core.Vault;

public static class VaultSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static ReadOnlySpan<byte> HeaderMacAssociatedData => "Fortiva.Header.MAC.v1"u8;
    private static ReadOnlySpan<byte> EntriesAssociatedData => "Fortiva.Entries.v1"u8;
    private static ReadOnlySpan<byte> IntegrityAssociatedData => "Fortiva.Integrity.v1"u8;

    public static byte[] ComputeHeaderMac(ReadOnlySpan<byte> vaultKey, VaultHeader header)
    {
        var payload = header.SerializeForMac();
        var mac = CngAesGcm.Seal(vaultKey, payload, HeaderMacAssociatedData);
        return mac;
    }

    public static void VerifyHeaderMac(ReadOnlySpan<byte> vaultKey, VaultHeader header)
    {
        try
        {
            var expected = header.SerializeForMac();
            var actual = CngAesGcm.Open(vaultKey, header.HeaderMac, HeaderMacAssociatedData);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new CryptographicException("Vault header MAC verification failed.");
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Vault header MAC verification failed.");
        }
    }

    private static void WriteBytes(BinaryWriter bw, byte[] data)
    {
        bw.Write(data.Length);
        bw.Write(data);
    }

    private static byte[] ReadBytes(BinaryReader br, int maxLength = VaultConstants.MaxFieldBytes)
    {
        var len = br.ReadInt32();
        if (len < 0 || len > maxLength)
            throw new InvalidDataException("Invalid vault blob length.");
        if (len > br.BaseStream.Length - br.BaseStream.Position)
            throw new InvalidDataException("Invalid vault blob length.");
        return br.ReadBytes(len);
    }

    public static byte[] SerializeVaultFile(VaultHeader header, byte[] encryptedEntries, byte[] encryptedIntegrity)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        var magic = Encoding.UTF8.GetBytes(VaultConstants.Magic);
        WriteBytes(bw, magic);
        bw.Write(header.FormatVersion);
        bw.Write(header.MinSupportedVersion);
        WriteBytes(bw, header.KdfParameters.ToBytes());
        bw.Write((byte)header.SecurityLevel);
        bw.Write(header.VaultId.ToByteArray());
        bw.Write(header.CreatedAt.UtcTicks);
        bw.Write(header.LastModifiedAt.UtcTicks);
        bw.Write(header.RevisionCounter);
        bw.Write(header.SecurityLevelCounter);
        WriteBytes(bw, header.Salt);
        WriteBytes(bw, header.WrappedVaultKey);
        WriteBytes(bw, header.HeaderMac);
        WriteBytes(bw, encryptedEntries);
        WriteBytes(bw, encryptedIntegrity);
        return ms.ToArray();
    }

    public static (VaultHeader header, byte[] encryptedEntries, byte[] encryptedIntegrity) ParseVaultFile(ReadOnlySpan<byte> data)
    {
        if (data.Length > VaultConstants.MaxVaultFileBytes)
            throw new InvalidDataException("Vault file exceeds maximum allowed size.");
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var magic = Encoding.UTF8.GetString(ReadBytes(br));
        if (magic != VaultConstants.Magic)
            throw new InvalidDataException("Invalid vault magic.");
        var header = new VaultHeader
        {
            FormatVersion = br.ReadByte(),
            MinSupportedVersion = br.ReadByte()
        };
        if (header.FormatVersion < VaultConstants.MinSupportedVersion)
            throw new InvalidDataException($"Vault version {header.FormatVersion} is not supported.");

        header.KdfParameters = Argon2Parameters.FromBytes(ReadBytes(br));
        header.SecurityLevel = (SecurityLevel)br.ReadByte();
        header.VaultId = new Guid(br.ReadBytes(16));
        header.CreatedAt = new DateTimeOffset(br.ReadInt64(), TimeSpan.Zero);
        header.LastModifiedAt = new DateTimeOffset(br.ReadInt64(), TimeSpan.Zero);
        header.RevisionCounter = br.ReadUInt64();
        header.SecurityLevelCounter = br.ReadUInt64();
        header.Salt = ReadBytes(br);
        header.WrappedVaultKey = ReadBytes(br);
        header.HeaderMac = ReadBytes(br);
        var encEntries = ReadBytes(br);
        var encIntegrity = ReadBytes(br);
        return (header, encEntries, encIntegrity);
    }

    public static (byte[] entriesBlob, byte[] integrityBlob) EncryptPayload(KeyHierarchy keys, VaultPayload payload)
    {
        var entriesJson = JsonSerializer.SerializeToUtf8Bytes(payload.Entries, JsonOptions);
        var integrityJson = JsonSerializer.SerializeToUtf8Bytes(payload.IntegrityLog, JsonOptions);
        return (
            keys.EncryptPayload(entriesJson, EntriesAssociatedData),
            keys.EncryptPayload(integrityJson, IntegrityAssociatedData));
    }

    public static VaultPayload DecryptPayload(KeyHierarchy keys, byte[] encryptedEntries, byte[] encryptedIntegrity)
    {
        var entriesJson = keys.DecryptPayload(encryptedEntries, EntriesAssociatedData);
        var integrityJson = keys.DecryptPayload(encryptedIntegrity, IntegrityAssociatedData);
        var entries = JsonSerializer.Deserialize<List<VaultEntry>>(entriesJson, JsonOptions) ?? [];
        var log = JsonSerializer.Deserialize<List<IntegrityLogEntry>>(integrityJson, JsonOptions) ?? [];
        return new VaultPayload { Entries = entries, IntegrityLog = log };
    }
}
