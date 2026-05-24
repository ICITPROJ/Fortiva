using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.Principal;

namespace Fortiva.Core.Audit;

public enum AuditEventType
{
    UnlockAttempt,
    UnlockSuccess,
    UnlockFailure,
    Lock,
    PolicyViolation,
    SnapshotRestore,
    ConfigurationChange,
    ExportAuditLog,
    SharedVaultAccess
}

public sealed class AuditEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public AuditEventType EventType { get; set; }
    public string Message { get; set; } = "";
    public string? UserSid { get; set; }
    public string? MachineName { get; set; } = Environment.MachineName;
    public bool Success { get; set; }
}

public sealed class AuditLogger
{
    private static readonly object FileLock = new();

    private readonly string _auditDirectory;
    private readonly byte[] _hmacKey;

    public static AuditLogger Default { get; } = new();

    public AuditLogger(string? logDirectory = null)
    {
        _auditDirectory = logDirectory ?? Path.Combine(
            Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva"),
            "audit");
        Directory.CreateDirectory(_auditDirectory);
        _hmacKey = AuditIntegrity.LoadOrCreateHmacKey(_auditDirectory);
    }

    public void Log(AuditEventType type, string message, bool success = true)
    {
        var evt = new AuditEvent
        {
            EventType = type,
            Message = message,
            Success = success,
            UserSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName
        };
        var json = JsonSerializer.Serialize(evt);
        var sig = AuditIntegrity.SignLine(json, _hmacKey);
        var line = json + "\t" + sig + Environment.NewLine;
        var logPath = GetLogPathForMonth(DateTime.UtcNow);

        lock (FileLock)
            File.AppendAllText(logPath, line);
    }

    public IReadOnlyList<AuditEvent> ReadRecent(int maxLines = 500)
    {
        var events = new List<AuditEvent>();
        string[] files;

        lock (FileLock)
            files = Directory.GetFiles(_auditDirectory, "audit-*.jsonl");

        foreach (var file in files.OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            foreach (var evt in ReadValidatedEvents(file))
                events.Add(evt);
        }

        return events
            .OrderByDescending(e => e.Timestamp)
            .Take(maxLines)
            .ToList();
    }

    public void ExportTo(string destinationPath)
    {
        var lines = new List<string>();
        string[] files;

        lock (FileLock)
            files = Directory.GetFiles(_auditDirectory, "audit-*.jsonl");

        foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            foreach (var rawLine in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var tab = rawLine.LastIndexOf('\t');
                if (tab < 0)
                    continue;

                var json = rawLine[..tab];
                var sig = rawLine[(tab + 1)..];
                if (AuditIntegrity.VerifyLine(json, sig, _hmacKey))
                    lines.Add(rawLine);
            }
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        lock (FileLock)
            File.WriteAllLines(destinationPath, lines);
    }

    private IEnumerable<AuditEvent> ReadValidatedEvents(string filePath)
    {
        if (!File.Exists(filePath))
            yield break;

        string[] lines;
        lock (FileLock)
            lines = File.ReadAllLines(filePath);

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var tab = rawLine.LastIndexOf('\t');
            if (tab < 0)
                continue;

            var json = rawLine[..tab];
            var sig = rawLine[(tab + 1)..];
            if (!AuditIntegrity.VerifyLine(json, sig, _hmacKey))
                continue;

            var evt = JsonSerializer.Deserialize<AuditEvent>(json);
            if (evt is not null)
                yield return evt;
        }
    }

    private string GetLogPathForMonth(DateTime utcNow) =>
        Path.Combine(_auditDirectory, $"audit-{utcNow:yyyy-MM}.jsonl");
}
