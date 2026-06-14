using System.Runtime.InteropServices;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Chromium on Windows fails to spawn native hosts whose manifest path contains spaces.
/// Use 8.3 short paths in native-messaging manifests when needed.
/// </summary>
public static class NativeMessagingPathHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    public static string ForNativeHostManifest(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return executablePath;

        var full = Path.GetFullPath(executablePath);
        if (!full.Contains(' ', StringComparison.Ordinal))
            return full;

        var sb = new StringBuilder(512);
        var written = GetShortPathName(full, sb, (uint)sb.Capacity);
        if (written == 0 || sb.Length == 0)
            return full;

        return sb.ToString();
    }

    public static bool PathsReferToSameFile(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        try
        {
            var fullA = Path.GetFullPath(a);
            var fullB = Path.GetFullPath(b);
            if (string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(
                ForNativeHostManifest(fullA),
                ForNativeHostManifest(fullB),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
