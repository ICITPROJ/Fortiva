using System.Globalization;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Crypto;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.ImportExport;

public enum ImportFormatKind
{
    GenericCsv,
    KeePassCsv,
    BrowserCsv,
    AppleKeychainCsv,
    EncryptedBackup
}

public static class ImportSourceLabels
{
    public static string For(ImportFormatKind kind) => kind switch
    {
        ImportFormatKind.GenericCsv => "CSV import",
        ImportFormatKind.KeePassCsv => "KeePass export",
        ImportFormatKind.BrowserCsv => "Browser export (Chrome / Edge / Firefox)",
        ImportFormatKind.AppleKeychainCsv => "Apple iPhone / iCloud Keychain export",
        ImportFormatKind.EncryptedBackup => "Fortiva encrypted backup",
        _ => "Import"
    };

    public static string SuggestDisplayName(ImportFormatKind kind, string? fileName)
    {
        var format = For(kind);
        var baseName = string.IsNullOrWhiteSpace(fileName)
            ? ""
            : Path.GetFileNameWithoutExtension(fileName.Trim());
        return string.IsNullOrWhiteSpace(baseName) ? format : $"{baseName} ({format})";
    }
}

/// <summary>User-provided labels and notes for an import batch.</summary>
public sealed class ImportBatchMetadata
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxSourceHintLength = 200;
    public const int MaxNotesLength = 4096;

    public string DisplayName { get; init; } = "";
    public string? SourceHint { get; init; }
    public string? Notes { get; init; }

    public static ImportBatchMetadata Create(
        string displayName,
        string? sourceHint,
        string? notes,
        string fallbackDisplayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? fallbackDisplayName : displayName.Trim();
        if (name.Length > MaxDisplayNameLength)
            throw new ArgumentException($"Import name exceeds {MaxDisplayNameLength} characters.");

        sourceHint = TrimOptional(sourceHint, MaxSourceHintLength, "Source");
        notes = TrimOptional(notes, MaxNotesLength, "Notes");

        return new ImportBatchMetadata
        {
            DisplayName = name,
            SourceHint = sourceHint,
            Notes = notes
        };
    }

    private static string? TrimOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentException($"{fieldName} exceed {maxLength} characters.");

        return value;
    }
}

public static class CsvImporter
{
    public static List<VaultEntry> ImportFromCsv(Stream stream, int maxBytes = 10 * 1024 * 1024, int maxRows = 10_000)
        => ImportCredentials(stream, ImportFormatKind.GenericCsv, maxBytes, maxRows)
            .Select(r => r.Entry)
            .ToList();

    public static List<ImportedCredential> ImportCredentials(
        Stream stream,
        ImportFormatKind format,
        int maxBytes = 10 * 1024 * 1024,
        int maxRows = 10_000)
    {
        if (stream.CanSeek && stream.Length > maxBytes)
            throw new InvalidDataException($"CSV import exceeds maximum size ({maxBytes} bytes).");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var headerLine = reader.ReadLine();
        if (headerLine is null) return [];

        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var titleIdx = IndexOf(headers, format == ImportFormatKind.AppleKeychainCsv
            ? ["title", "name", "website name"]
            : ["name", "title"]);
        var userIdx = IndexOf(headers, "username", "login", "user");
        var passIdx = IndexOf(headers, "password", "pass");
        var urlIdx = IndexOf(headers, "url", "uri", "website", "site");
        var notesIdx = IndexOf(headers, "notes", "extra", "note");
        var createdIdx = IndexOf(headers,
            "date created", "date_created", "created", "creation date", "created at", "create time");
        var lastUsedIdx = IndexOf(headers,
            "date last used", "date_last_used", "last used", "last_used", "modified", "last modified", "updated");

        var rows = new List<ImportedCredential>();
        string? line;
        var rowNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (rows.Count >= maxRows)
                throw new InvalidDataException($"CSV import exceeds maximum row count ({maxRows}).");
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = ParseCsvLine(line);
            var title = CheckField(Get(cols, titleIdx), "title", rowNumber);
            var username = CheckField(Get(cols, userIdx), "username", rowNumber);
            var password = CheckField(Get(cols, passIdx), "password", rowNumber);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
                continue;

            rows.Add(new ImportedCredential
            {
                Entry = new VaultEntry
                {
                    Title = title,
                    Username = username,
                    Password = password,
                    Url = CheckField(Get(cols, urlIdx), "url", rowNumber),
                    Notes = CheckField(Get(cols, notesIdx), "notes", rowNumber)
                },
                SourceCreatedAt = ParseOptionalDate(Get(cols, createdIdx)),
                SourceLastUsedAt = ParseOptionalDate(Get(cols, lastUsedIdx))
            });
        }

        return rows;
    }

    private const int MaxFieldLength = 64 * 1024;

    private static string CheckField(string value, string fieldName, int rowNumber)
    {
        if (value.Length > MaxFieldLength)
            throw new InvalidDataException(
                $"CSV row {rowNumber}: the '{fieldName}' field exceeds the maximum length "
                + $"({MaxFieldLength} characters). Import aborted so no data is silently truncated.");
        return value;
    }

    public static DateTimeOffset? ParseOptionalDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            try
            {
                return raw.Length > 10
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch
            {
                /* fall through */
            }
        }

        string[] formats =
        [
            "O", "o",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-dd",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy"
        ];

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto;

        foreach (var fmt in formats)
        {
            if (DateTimeOffset.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                return dto;
            if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null;
    }

    private static int IndexOf(List<string> headers, params string[] names)
    {
        foreach (var n in names)
        {
            var i = headers.IndexOf(n);
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string Get(List<string> cols, int idx) => idx >= 0 && idx < cols.Count ? cols[idx] : "";

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}

public static class KeePassImporter
{
    public static List<ImportedCredential> ImportCredentials(Stream stream)
        => CsvImporter.ImportCredentials(stream, ImportFormatKind.KeePassCsv);
}

public static class AppleKeychainImporter
{
    /// <summary>CSV from Safari / iPhone keychain exporters (Title, Website, Username, Password, Notes, Created).</summary>
    public static List<ImportedCredential> ImportCredentials(Stream stream)
        => CsvImporter.ImportCredentials(stream, ImportFormatKind.AppleKeychainCsv);
}

public static class EncryptedBackupImporter
{
    private const int MaxBackupJsonBytes = 32 * 1024 * 1024;

    public static List<ImportedCredential> ImportCredentials(Stream stream, string backupPassword)
    {
        if (stream.CanSeek && stream.Length > MaxBackupJsonBytes)
            throw new InvalidDataException($"Backup file exceeds maximum size ({MaxBackupJsonBytes} bytes).");

        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxBackupJsonBytes)
                throw new InvalidDataException($"Backup file exceeds maximum size ({MaxBackupJsonBytes} bytes).");
            ms.Write(buffer, 0, read);
        }

        using var doc = JsonDocument.Parse(ms.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        var root = doc.RootElement;
        if (!root.TryGetProperty("salt", out var saltEl) || !root.TryGetProperty("data", out var dataEl))
            throw new InvalidDataException("Not a valid Fortiva encrypted backup file.");

        var salt = Convert.FromBase64String(saltEl.GetString() ?? "");
        var blob = Convert.FromBase64String(dataEl.GetString() ?? "");
        var (mk, _) = Argon2Kdf.DeriveMasterKey(backupPassword, Argon2Parameters.PersonalDefault, salt);
        try
        {
            var payloadJson = CngAesGcm.Open(mk, blob);
            var payload = JsonSerializer.Deserialize<VaultPayload>(payloadJson)
                ?? throw new InvalidDataException("Backup payload is empty.");
            return payload.Entries.Select(e => new ImportedCredential
            {
                Entry = e.Clone(),
                SourceCreatedAt = e.SourceCreatedAt ?? e.CreatedAt,
                SourceLastUsedAt = e.SourceLastUsedAt ?? e.ModifiedAt
            }).ToList();
        }
        finally
        {
            SecureMemory.Zero(mk);
        }
    }
}

public static class VaultImporter
{
    public static List<ImportedCredential> ImportCredentials(
        Stream stream,
        ImportFormatKind format,
        string? backupPassword = null)
    {
        return format switch
        {
            ImportFormatKind.KeePassCsv => KeePassImporter.ImportCredentials(stream),
            ImportFormatKind.AppleKeychainCsv => AppleKeychainImporter.ImportCredentials(stream),
            ImportFormatKind.EncryptedBackup => EncryptedBackupImporter.ImportCredentials(
                stream, backupPassword ?? throw new ArgumentException("Backup password required.", nameof(backupPassword))),
            ImportFormatKind.BrowserCsv => CsvImporter.ImportCredentials(stream, ImportFormatKind.BrowserCsv),
            _ => CsvImporter.ImportCredentials(stream, ImportFormatKind.GenericCsv)
        };
    }
}

public static class VaultExporter
{
    public static byte[] ExportEncrypted(VaultUnlockContext ctx, string exportPassword)
    {
        var exportPayload = JsonSerializer.SerializeToUtf8Bytes(ctx.Payload);
        var (mk, salt) = Argon2Kdf.DeriveMasterKey(exportPassword, Argon2Parameters.PersonalDefault);
        var sealedBlob = CngAesGcm.Seal(mk, exportPayload);
        SecureMemory.Zero(mk);
        var wrapper = new { salt, data = Convert.ToBase64String(sealedBlob), version = 1 };
        return JsonSerializer.SerializeToUtf8Bytes(wrapper);
    }

    public static string ExportPlaintextCsv(VaultUnlockContext ctx, FortivaPolicy? policy = null)
    {
        if (!PolicyEnforcer.CanExportPlaintext(policy))
            throw new InvalidOperationException("Plaintext export is disabled by policy.");

        var sb = new StringBuilder();
        sb.AppendLine("title,username,password,url,notes");
        foreach (var e in ctx.Payload.Entries)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(e.Title),
                CsvEscape(e.Username),
                CsvEscape(e.Password),
                CsvEscape(e.Url),
                CsvEscape(e.Notes)));
        }
        return sb.ToString();
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}
