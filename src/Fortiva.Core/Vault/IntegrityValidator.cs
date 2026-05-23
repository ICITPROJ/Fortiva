using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fortiva.Core.Vault;

public static class IntegrityValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] HashEntry(VaultEntry entry)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            entry.Id,
            entry.Title,
            entry.Username,
            entry.Password,
            entry.Url,
            entry.Notes,
            entry.TotpSecret,
            entry.PasskeyCredentialId,
            entry.PasskeyRpId,
            entry.ModifiedAt,
            entry.IsSecureNote,
            entry.IsFavorite,
            entry.Tags
        }, JsonOptions);
        return SHA256.HashData(json);
    }

    // Actions that are expected to reference entry IDs that may have been subsequently removed.
    private static readonly HashSet<string> TombstoneActions = ["delete", "update", "import"];

    public static void ValidateConsistency(VaultPayload payload)
    {
        if (payload.IntegrityLog.Count == 0 && payload.Entries.Count > 0)
            return; // legacy / first write

        var deletedIds = payload.IntegrityLog
            .Where(l => l.Action == "delete" && l.EntryId.HasValue)
            .Select(l => l.EntryId!.Value)
            .ToHashSet();

        var entryById = payload.Entries.ToDictionary(e => e.Id);
        foreach (var log in payload.IntegrityLog)
        {
            if (log.EntryId is not { } id) continue;
            if (entryById.ContainsKey(id)) continue;
            if (TombstoneActions.Contains(log.Action) || deletedIds.Contains(id)) continue;
            throw new InvalidDataException($"Integrity log references missing entry {id}.");
        }

        foreach (var entry in payload.Entries)
        {
            var latest = payload.IntegrityLog
                .Where(l => l.EntryId == entry.Id && l.EntryHash.Length > 0)
                .LastOrDefault();
            if (latest is null) continue;

            var expected = HashEntry(entry);
            if (CryptographicOperations.FixedTimeEquals(latest.EntryHash, expected))
                continue;

            if (CryptographicOperations.FixedTimeEquals(latest.EntryHash, HashEntryLegacy(entry)))
                continue;

            throw new InvalidDataException($"Integrity hash mismatch for entry {entry.Id}.");
        }
    }

    private static byte[] HashEntryLegacy(VaultEntry entry)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            entry.Id,
            entry.Title,
            entry.Username,
            entry.Url,
            entry.ModifiedAt,
            entry.IsSecureNote
        }, JsonOptions);
        return SHA256.HashData(json);
    }

    public static IntegrityLogEntry CreateLogEntry(string action, Guid? entryId, ulong revision, VaultEntry? entry = null)
    {
        return new IntegrityLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Action = action,
            EntryId = entryId,
            RevisionAfter = revision,
            EntryHash = entry is not null ? HashEntry(entry) : []
        };
    }
}
