namespace Fortiva.Core.Platform;

/// <summary>Best-effort append-only diagnostic log for Core components (bridge host, forwarder).</summary>
public static class FortivaDiagnosticLog
{
    public static void Write(string category, Exception ex)
    {
        try
        {
            var dir = FortivaPaths.PersonalCrashLogDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "fortiva-core.log");
            var line = $"[{DateTimeOffset.UtcNow:O}] {category}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            /* best effort */
        }
    }
}
