using System.Globalization;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Crypto;
using Fortiva.Core.Policy;
using Fortiva.Core.Vault;

namespace Fortiva.Core.ImportExport;

public static class CsvImporter
{
    public static List<VaultEntry> ImportFromCsv(Stream stream, int maxBytes = 10 * 1024 * 1024, int maxRows = 10_000)
    {
        if (stream.CanSeek && stream.Length > maxBytes)
            throw new InvalidDataException($"CSV import exceeds maximum size ({maxBytes} bytes).");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var headerLine = reader.ReadLine();
        if (headerLine is null) return [];

        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var titleIdx = IndexOf(headers, "name", "title");
        var userIdx = IndexOf(headers, "username", "login");
        var passIdx = IndexOf(headers, "password");
        var urlIdx = IndexOf(headers, "url", "uri", "website");
        var notesIdx = IndexOf(headers, "notes", "extra");

        var entries = new List<VaultEntry>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (entries.Count >= maxRows)
                throw new InvalidDataException($"CSV import exceeds maximum row count ({maxRows}).");
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            entries.Add(new VaultEntry
            {
                Title = TrimField(Get(cols, titleIdx)),
                Username = TrimField(Get(cols, userIdx)),
                Password = TrimField(Get(cols, passIdx)),
                Url = TrimField(Get(cols, urlIdx)),
                Notes = TrimField(Get(cols, notesIdx))
            });
        }
        return entries;
    }

    private const int MaxFieldLength = 4096;

    private static string TrimField(string value)
        => value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];

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
    /// <summary>Imports KeePass CSV export (Group, Title, Username, Password, URL, Notes).</summary>
    public static List<VaultEntry> ImportFromKeePassCsv(Stream stream) => CsvImporter.ImportFromCsv(stream);
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
