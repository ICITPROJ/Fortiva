using System.Text.Json;

namespace Fortiva.Core.Vault;

/// <summary>
/// Written when portable two-way sync could not fully roll back — blocks silent re-sync until cleared.
/// </summary>
public static class VaultSyncMarker
{
    public const string DivergenceFileName = "sync-divergence.json";
    public const string InProgressFileName = "sync-in-progress.json";

    public sealed class Marker
    {
        public DateTimeOffset At { get; init; }
        public string Message { get; init; } = "";
        public string? LocalVaultDirectory { get; init; }
        public string? RemoteVaultDirectory { get; init; }
    }

    public static string GetDivergencePath(string vaultDirectory)
        => Path.Combine(vaultDirectory, DivergenceFileName);

    public static string GetInProgressPath(string vaultDirectory)
        => Path.Combine(vaultDirectory, InProgressFileName);

    public static bool Exists(string vaultDirectory)
        => File.Exists(GetDivergencePath(vaultDirectory))
           || File.Exists(GetInProgressPath(vaultDirectory));

    public static bool HasDivergence(string vaultDirectory)
        => File.Exists(GetDivergencePath(vaultDirectory));

    public static bool HasInProgress(string vaultDirectory)
        => File.Exists(GetInProgressPath(vaultDirectory));

    public static Marker? Read(string vaultDirectory)
    {
        foreach (var path in new[] { GetDivergencePath(vaultDirectory), GetInProgressPath(vaultDirectory) })
        {
            if (!File.Exists(path))
                continue;

            try
            {
                return JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
            }
            catch
            {
                return new Marker { At = DateTimeOffset.UtcNow, Message = "Sync marker is corrupt." };
            }
        }

        return null;
    }

    public static void WriteDivergence(string vaultDirectory, string message, string? localDir, string? remoteDir)
        => WriteMarker(GetDivergencePath(vaultDirectory), message, localDir, remoteDir);

    public static void WriteInProgress(string vaultDirectory, string? localDir, string? remoteDir)
        => WriteMarker(
            GetInProgressPath(vaultDirectory),
            "Portable sync in progress — do not disconnect the drive or close Fortiva.",
            localDir,
            remoteDir);

    private static void WriteMarker(string path, string message, string? localDir, string? remoteDir)
    {
        var marker = new Marker
        {
            At = DateTimeOffset.UtcNow,
            Message = message,
            LocalVaultDirectory = localDir,
            RemoteVaultDirectory = remoteDir
        };
        File.WriteAllText(path, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Clear(string vaultDirectory)
    {
        foreach (var path in new[] { GetDivergencePath(vaultDirectory), GetInProgressPath(vaultDirectory) })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static void ClearBoth(string localVaultDirectory, string remoteVaultDirectory)
    {
        Clear(localVaultDirectory);
        Clear(remoteVaultDirectory);
    }

    public static void WriteInProgressBoth(string localVaultDirectory, string remoteVaultDirectory)
    {
        WriteInProgress(localVaultDirectory, localVaultDirectory, remoteVaultDirectory);
        WriteInProgress(remoteVaultDirectory, localVaultDirectory, remoteVaultDirectory);
    }
}
