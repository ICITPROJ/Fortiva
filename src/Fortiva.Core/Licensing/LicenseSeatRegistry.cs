using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Fortiva.Core.Licensing;

/// <summary>
/// Tracks Enterprise seat usage against license MaxSeats. Stored under %PROGRAMDATA%\Fortiva.
/// </summary>
public static class LicenseSeatRegistry
{
    private const string SeatEntropy = "Fortiva.Seats.v1";
    private static readonly TimeSpan SeatStaleAfter = TimeSpan.FromDays(90);
    /// <summary>Test hook — override seat registry path.</summary>
    public static string? SeatFilePathOverride { get; set; }

    public static string SeatFilePath =>
        SeatFilePathOverride ??
        Path.Combine(Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva"), "seats.dat");

    public sealed class SeatRecord
    {
        public string MachineId { get; set; } = "";
        public string UserSid { get; set; } = "";
        public DateTimeOffset LastSeen { get; set; }
    }

    public sealed class SeatRegistry
    {
        public List<SeatRecord> Seats { get; set; } = [];
    }

    public static void RegisterCurrentSeat(SignedLicense license)
    {
        var registry = Load();
        var machineId = GetMachineId();
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var existing = registry.Seats.FirstOrDefault(s =>
            s.MachineId == machineId && s.UserSid == userSid);
        if (existing is not null)
            existing.LastSeen = DateTimeOffset.UtcNow;
        else
        {
            EnsureWithinLimit(registry, license);
            registry.Seats.Add(new SeatRecord { MachineId = machineId, UserSid = userSid, LastSeen = DateTimeOffset.UtcNow });
        }

        PruneStale(registry);
        Save(registry);
    }

    public static void EnsureSeatAvailable(SignedLicense license)
    {
        var registry = Load();
        PruneStale(registry);
        var machineId = GetMachineId();
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var alreadyRegistered = registry.Seats.Any(s =>
            s.MachineId == machineId && s.UserSid == userSid);
        if (alreadyRegistered)
            return;
        EnsureWithinLimit(registry, license);
    }

    public static int CountActiveSeats()
    {
        var registry = Load();
        PruneStale(registry);
        return registry.Seats.Count;
    }

    private static void EnsureWithinLimit(SeatRegistry registry, SignedLicense license)
    {
        var max = license.Document.MaxSeats;
        if (max <= 0)
            return;
        if (registry.Seats.Count >= max)
            throw new InvalidOperationException(
                $"This license allows {max} seats. All seats are in use. Contact your administrator.");
    }

    private static void PruneStale(SeatRegistry registry)
    {
        var cutoff = DateTimeOffset.UtcNow - SeatStaleAfter;
        registry.Seats.RemoveAll(s => s.LastSeen < cutoff);
    }

    private static SeatRegistry Load()
    {
        if (!File.Exists(SeatFilePath))
            return new SeatRegistry();
        try
        {
            var protectedBytes = File.ReadAllBytes(SeatFilePath);
            var json = ProtectedData.Unprotect(
                protectedBytes,
                Encoding.UTF8.GetBytes(SeatEntropy),
                DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<SeatRegistry>(json) ?? new SeatRegistry();
        }
        catch
        {
            return new SeatRegistry();
        }
    }

    private static void Save(SeatRegistry registry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SeatFilePath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(registry);
        var protectedBytes = ProtectedData.Protect(
            json,
            Encoding.UTF8.GetBytes(SeatEntropy),
            DataProtectionScope.LocalMachine);
        var temp = SeatFilePath + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, SeatFilePath, overwrite: true);
    }

    internal static string GetMachineId()
    {
        var raw = $"{Environment.MachineName}|{Environment.OSVersion.Version}|{Environment.UserDomainName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
