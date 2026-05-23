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
    private readonly string _logPath;
    private readonly string _auditDirectory;
    private readonly byte[] _hmacKey;
    private readonly object _lock = new();

    public AuditLogger(string? logDirectory = null)
    {
        _auditDirectory = logDirectory ?? Path.Combine(
            Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva"),
            "audit");
        Directory.CreateDirectory(_auditDirectory);
        _logPath = Path.Combine(_auditDirectory, $"audit-{DateTime.UtcNow:yyyy-MM}.jsonl");
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
        lock (_lock)
            File.AppendAllText(_logPath, line);
    }

    public IReadOnlyList<AuditEvent> ReadRecent(int maxLines = 500)
    {
        if (!File.Exists(_logPath)) return [];
        var events = new List<AuditEvent>();
        foreach (var rawLine in File.ReadAllLines(_logPath).TakeLast(maxLines))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var tab = rawLine.LastIndexOf('\t');
            if (tab < 0) continue;
            var json = rawLine[..tab];
            var sig = rawLine[(tab + 1)..];
            if (!AuditIntegrity.VerifyLine(json, sig, _hmacKey))
                continue;
            var evt = JsonSerializer.Deserialize<AuditEvent>(json);
            if (evt is not null) events.Add(evt);
        }
        return events;
    }

    public void ExportTo(string destinationPath)
    {
        if (File.Exists(_logPath))
            File.Copy(_logPath, destinationPath, overwrite: true);
    }
}
