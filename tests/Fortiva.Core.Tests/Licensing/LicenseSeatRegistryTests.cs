using Fortiva.Core.Licensing;

namespace Fortiva.Core.Tests.Licensing;

public class LicenseSeatRegistryTests : IDisposable
{
    private readonly string _seatFile;

    public LicenseSeatRegistryTests()
    {
        _seatFile = Path.Combine(Path.GetTempPath(), $"fortiva-seats-{Guid.NewGuid():N}.dat");
        LicenseSeatRegistry.SeatFilePathOverride = _seatFile;
    }

    public void Dispose()
    {
        LicenseSeatRegistry.SeatFilePathOverride = null;
        try { if (File.Exists(_seatFile)) File.Delete(_seatFile); } catch { }
    }

    private static SignedLicense CreateLicense(int maxSeats) =>
        new()
        {
            Document = new LicenseDocument
            {
                CompanyName = "Seat Test",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                MaxSeats = maxSeats
            },
            Signature = []
        };

    [Fact]
    public void EnsureSeatAvailable_BlocksWhenAllSeatsUsed()
    {
        var license = CreateLicense(1);
        SaveRegistry(new LicenseSeatRegistry.SeatRegistry
        {
            Seats =
            [
                new LicenseSeatRegistry.SeatRecord
                {
                    MachineId = "other-machine",
                    UserSid = "other-user",
                    LastSeen = DateTimeOffset.UtcNow
                }
            ]
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LicenseSeatRegistry.EnsureSeatAvailable(license));
        Assert.Contains("seats", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterCurrentSeat_AllowsSameUserRefresh()
    {
        var license = CreateLicense(1);
        LicenseSeatRegistry.RegisterCurrentSeat(license);
        LicenseSeatRegistry.RegisterCurrentSeat(license);
        Assert.Equal(1, LicenseSeatRegistry.CountActiveSeats());
    }

    private static void SaveRegistry(LicenseSeatRegistry.SeatRegistry registry)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(registry);
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            json,
            System.Text.Encoding.UTF8.GetBytes("Fortiva.Seats.v1"),
            System.Security.Cryptography.DataProtectionScope.LocalMachine);
        File.WriteAllBytes(LicenseSeatRegistry.SeatFilePath, protectedBytes);
    }
}
