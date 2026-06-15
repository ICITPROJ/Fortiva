using Fortiva.Core.Platform;

namespace Fortiva.AppHost.Services;

internal static class HelloSetupLog
{
    internal static void Step(string message)
    {
        try
        {
            var dir = FortivaPaths.PersonalCrashLogDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "fortiva-crash.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] HelloSetup: {message}{Environment.NewLine}");
        }
        catch
        {
            /* best effort */
        }
    }

    internal static void Error(string stage, Exception ex)
    {
        Step($"{stage} failed: {ex.GetType().Name}: {ex.Message}");
        App.LogException($"HelloSetup.{stage}", ex);
    }
}
