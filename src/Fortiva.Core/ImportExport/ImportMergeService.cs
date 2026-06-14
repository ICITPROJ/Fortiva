using System.Security.Cryptography;
using System.Text;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Vault;

namespace Fortiva.Core.ImportExport;

public enum ImportItemKind
{
    New,
    Duplicate,
    Conflict
}

public enum ImportConflictChoice
{
    KeepExisting,
    UseImported,
    KeepBoth
}

/// <summary>One credential row from an export file, with optional source dates.</summary>
public sealed class ImportedCredential
{
    public VaultEntry Entry { get; init; } = new();
    public DateTimeOffset? SourceCreatedAt { get; init; }
    public DateTimeOffset? SourceLastUsedAt { get; init; }
}

public sealed class ImportPreviewItem
{
    public ImportItemKind Kind { get; init; }
    public ImportedCredential Incoming { get; init; } = null!;
    public VaultEntry? Existing { get; init; }
    public ImportConflictChoice Resolution { get; set; } = ImportConflictChoice.KeepExisting;
}

public sealed class ImportPreview
{
    public List<ImportPreviewItem> Items { get; init; } = [];

    public int NewCount => Items.Count(i => i.Kind == ImportItemKind.New);
    public int DuplicateCount => Items.Count(i => i.Kind == ImportItemKind.Duplicate);
    public int ConflictCount => Items.Count(i => i.Kind == ImportItemKind.Conflict);
}

public sealed class ImportApplyPlan
{
    public ImportBatch Batch { get; init; } = null!;
    public List<VaultEntry> ToAdd { get; init; } = [];
    public List<VaultEntry> ToUpdate { get; init; } = [];
    public int SkippedDuplicateCount { get; init; }
    public int ConflictKeptExistingCount { get; init; }
    public int ConflictUpdatedCount { get; init; }
    public int ConflictKeptBothCount { get; init; }
}

public static class ImportMergeService
{
    public static ImportPreview Analyze(IReadOnlyList<VaultEntry> existing, IReadOnlyList<ImportedCredential> incoming)
    {
        var byKey = new Dictionary<string, List<VaultEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in existing)
        {
            var key = BuildMatchKey(entry);
            if (string.IsNullOrEmpty(key)) continue;
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }
            list.Add(entry);
        }

        var items = new List<ImportPreviewItem>();
        var seenIncoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in incoming)
        {
            var key = BuildMatchKey(row.Entry);
            if (string.IsNullOrEmpty(key) || !byKey.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                if (!string.IsNullOrEmpty(key) && !seenIncoming.Add(key))
                {
                    items.Add(new ImportPreviewItem
                    {
                        Kind = ImportItemKind.Duplicate,
                        Incoming = row
                    });
                    continue;
                }

                items.Add(new ImportPreviewItem { Kind = ImportItemKind.New, Incoming = row });
                continue;
            }

            var match = PickExistingMatch(matches, row.Entry);
            if (PasswordsEqual(match.Password, row.Entry.Password))
            {
                items.Add(new ImportPreviewItem
                {
                    Kind = ImportItemKind.Duplicate,
                    Incoming = row,
                    Existing = match
                });
                continue;
            }

            items.Add(new ImportPreviewItem
            {
                Kind = ImportItemKind.Conflict,
                Incoming = row,
                Existing = match,
                Resolution = ImportConflictChoice.KeepExisting
            });
        }

        return new ImportPreview { Items = items };
    }

    public static ImportApplyPlan BuildApplyPlan(
        ImportPreview preview,
        string sourceLabel,
        string format,
        string? fileName,
        ImportBatchMetadata? metadata = null)
    {
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            ImportedAt = DateTimeOffset.UtcNow,
            SourceLabel = sourceLabel,
            DisplayName = metadata?.DisplayName ?? "",
            SourceHint = metadata?.SourceHint,
            Notes = metadata?.Notes,
            FileName = fileName,
            Format = format
        };

        var toAdd = new List<VaultEntry>();
        var toUpdate = new List<VaultEntry>();
        var skippedDuplicates = 0;
        var skippedDuplicateRecords = new List<ImportDuplicateRecord>();
        var keptExisting = 0;
        var updated = 0;
        var keptBoth = 0;

        foreach (var item in preview.Items)
        {
            switch (item.Kind)
            {
                case ImportItemKind.New:
                    toAdd.Add(PrepareNewEntry(item.Incoming, batch));
                    break;

                case ImportItemKind.Duplicate:
                    skippedDuplicates++;
                    skippedDuplicateRecords.Add(ToDuplicateRecord(item));
                    break;

                case ImportItemKind.Conflict:
                    switch (item.Resolution)
                    {
                        case ImportConflictChoice.UseImported when item.Existing is not null:
                            toUpdate.Add(ApplyImportedOverExisting(item.Existing, item.Incoming, batch));
                            updated++;
                            break;
                        case ImportConflictChoice.KeepBoth:
                            toAdd.Add(PrepareNewEntry(item.Incoming, batch));
                            keptBoth++;
                            break;
                        default:
                            keptExisting++;
                            break;
                    }
                    break;
            }
        }

        batch.AddedCount = toAdd.Count;
        batch.SkippedDuplicateCount = skippedDuplicates;
        batch.SkippedDuplicates = skippedDuplicateRecords;
        batch.ConflictKeptExistingCount = keptExisting;
        batch.ConflictUpdatedCount = updated;
        batch.ConflictKeptBothCount = keptBoth;

        return new ImportApplyPlan
        {
            Batch = batch,
            ToAdd = toAdd,
            ToUpdate = toUpdate,
            SkippedDuplicateCount = skippedDuplicates,
            ConflictKeptExistingCount = keptExisting,
            ConflictUpdatedCount = updated,
            ConflictKeptBothCount = keptBoth
        };
    }

    private static VaultEntry PickExistingMatch(IReadOnlyList<VaultEntry> matches, VaultEntry incoming)
    {
        var passwordMatch = matches.FirstOrDefault(m => PasswordsEqual(m.Password, incoming.Password));
        if (passwordMatch is not null)
            return passwordMatch;

        return matches
            .OrderByDescending(m => m.ModifiedAt)
            .First();
    }

    private static ImportDuplicateRecord ToDuplicateRecord(ImportPreviewItem item)
    {
        var incoming = item.Incoming.Entry;
        return new ImportDuplicateRecord
        {
            Title = incoming.Title,
            Username = incoming.Username,
            Url = incoming.Url ?? "",
            ExistingEntryId = item.Existing?.Id
        };
    }

    public static string BuildMatchKey(VaultEntry entry)
    {
        var host = ExtractHost(entry.Url);
        if (string.IsNullOrWhiteSpace(host))
            host = entry.Title.Trim();
        if (string.IsNullOrWhiteSpace(host))
            return "";

        return $"{host.ToLowerInvariant()}|{entry.Username.Trim().ToLowerInvariant()}";
    }

    public static string ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + url.Trim(), UriKind.Absolute, out uri))
                return url.Trim().ToLowerInvariant();
        }

        return DomainSafety.NormalizeHost(uri.Host);
    }

    private static VaultEntry PrepareNewEntry(ImportedCredential row, ImportBatch batch)
    {
        var entry = row.Entry.Clone();
        NormalizeImportedUrl(entry);
        entry.Id = Guid.NewGuid();
        entry.ImportSource = batch.ProvenanceLabel;
        entry.ImportBatchId = batch.Id;
        entry.ImportedAt = batch.ImportedAt;
        entry.SourceCreatedAt = row.SourceCreatedAt;
        entry.SourceLastUsedAt = row.SourceLastUsedAt;

        var created = row.SourceCreatedAt ?? batch.ImportedAt;
        entry.CreatedAt = created;
        entry.ModifiedAt = row.SourceLastUsedAt ?? created;
        if (!string.IsNullOrEmpty(entry.Password))
            entry.PasswordLastChanged = row.SourceLastUsedAt ?? created;

        entry.Tags = NormalizeImportTag(entry.Tags, batch.ProvenanceLabel);
        return entry;
    }

    private static VaultEntry ApplyImportedOverExisting(VaultEntry existing, ImportedCredential row, ImportBatch batch)
    {
        var updated = existing.Clone();
        var incoming = row.Entry;

        updated.Password = incoming.Password;
        if (!string.IsNullOrWhiteSpace(incoming.Title))
            updated.Title = incoming.Title;
        if (!string.IsNullOrWhiteSpace(incoming.Url))
            updated.Url = incoming.Url;
        if (!string.IsNullOrWhiteSpace(incoming.Notes))
            updated.Notes = incoming.Notes;
        if (!string.IsNullOrWhiteSpace(incoming.Username))
            updated.Username = incoming.Username;

        updated.ModifiedAt = DateTimeOffset.UtcNow;
        updated.PasswordLastChanged = row.SourceLastUsedAt ?? DateTimeOffset.UtcNow;
        updated.ImportedAt = batch.ImportedAt;
        updated.ImportBatchId = batch.Id;
        if (string.IsNullOrWhiteSpace(updated.ImportSource))
            updated.ImportSource = batch.ProvenanceLabel;
        if (row.SourceCreatedAt.HasValue && !updated.SourceCreatedAt.HasValue)
            updated.SourceCreatedAt = row.SourceCreatedAt;
        if (row.SourceLastUsedAt.HasValue)
            updated.SourceLastUsedAt = row.SourceLastUsedAt;

        updated.Tags = NormalizeImportTag(updated.Tags, batch.ProvenanceLabel);
        return updated;
    }

    internal static void NormalizeImportedUrl(VaultEntry entry)
        => VaultEntryWebsite.NormalizeWebsite(entry);

    private static List<string> NormalizeImportTag(List<string>? tags, string sourceLabel)
    {
        var result = new List<string>(tags ?? []);
        var importTag = $"import:{sourceLabel}";
        if (!result.Any(t => string.Equals(t, importTag, StringComparison.OrdinalIgnoreCase)))
            result.Add(importTag);
        return result;
    }

    private static bool PasswordsEqual(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
