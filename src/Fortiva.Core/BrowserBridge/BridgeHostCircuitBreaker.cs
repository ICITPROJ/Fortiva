namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Prevents native-host restart storms when Chrome respawns the host faster than session setup.
/// </summary>
public static class BridgeHostCircuitBreaker
{
    public const int MaxExitsInWindow = 5;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Backoff = TimeSpan.FromSeconds(10);

    private static string? _stateFileOverride;

    internal static void ConfigureStateFileForTests(string? path) => _stateFileOverride = path;

    public static int GetBackoffMilliseconds(bool enterprise)
    {
        var timestamps = LoadTimestamps(enterprise);
        Prune(timestamps, DateTime.UtcNow);
        return timestamps.Count >= MaxExitsInWindow ? (int)Backoff.TotalMilliseconds : 0;
    }

    public static void RecordExit(bool enterprise)
    {
        var timestamps = LoadTimestamps(enterprise);
        var now = DateTime.UtcNow;
        Prune(timestamps, now);
        timestamps.Add(now);
        SaveTimestamps(enterprise, timestamps);
    }

    private static void Prune(List<DateTime> timestamps, DateTime now)
    {
        var cutoff = now - Window;
        timestamps.RemoveAll(t => t < cutoff);
    }

    private static string StateFilePath(bool enterprise)
    {
        if (!string.IsNullOrWhiteSpace(_stateFileOverride))
            return _stateFileOverride!;

        var app = enterprise ? "FortivaEnterprise" : "FortivaPersonal";
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            app);
        return Path.Combine(root, "bridge-host-exit-window.json");
    }

    private static List<DateTime> LoadTimestamps(bool enterprise)
    {
        var path = StateFilePath(enterprise);
        if (!File.Exists(path))
            return new List<DateTime>();

        try
        {
            var json = File.ReadAllText(path);
            var parsed = BridgeJson.Deserialize<List<DateTime>>(json);
            return parsed ?? new List<DateTime>();
        }
        catch
        {
            return new List<DateTime>();
        }
    }

    private static void SaveTimestamps(bool enterprise, List<DateTime> timestamps)
    {
        try
        {
            var path = StateFilePath(enterprise);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, BridgeJson.Serialize(timestamps));
        }
        catch
        {
            /* best effort — never block host exit */
        }
    }
}
