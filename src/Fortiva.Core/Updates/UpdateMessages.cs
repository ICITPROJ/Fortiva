using System.Net.Http;
using System.Net.Sockets;

namespace Fortiva.Core.Updates;

public static class UpdateMessages
{
    public const string ManifestUnavailable =
        "Could not verify updates right now. Check your internet connection and try again later.";

    public static string ForCheckFailure(Exception ex)
    {
        var root = Unwrap(ex);
        return root switch
        {
            HttpRequestException { InnerException: SocketException se } => ForSocket(se),
            SocketException se => ForSocket(se),
            HttpRequestException http => ForHttp(http),
            TaskCanceledException => "Update check timed out. Check your internet connection and try again.",
            _ when IsHostUnknown(root) =>
                "Could not reach the Fortiva update server. Check your internet connection and try again later.",
            _ => "Could not check for updates. Check your internet connection and try again later."
        };
    }

    public static string OfflineUpToDate(string version)
        => $"You have the latest version ({version}). The update server is temporarily unreachable.";

    public static string OfflineUpdateAvailable(string version)
        => $"Version {version} is available. Connect to the internet and check again to download it.";

    public static string ForApplyFailure(Exception ex)
    {
        var root = Unwrap(ex);
        var message = root.Message;

        if (message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("integrity check", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("changed after verification", StringComparison.OrdinalIgnoreCase))
            return "The downloaded installer failed verification. Try checking for updates again.";

        if (message.Contains("Authenticode", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not Authenticode-signed", StringComparison.OrdinalIgnoreCase))
            return "The update installer is not signed by icmclab studio. Installation was blocked for your safety.";

        if (message.Contains("Lock the vault", StringComparison.OrdinalIgnoreCase))
            return "Lock the vault before installing an update.";

        if (root is HttpRequestException or SocketException or TaskCanceledException)
            return ForCheckFailure(ex);

        if (message.Contains("host is not allowed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("path is not allowed", StringComparison.OrdinalIgnoreCase))
            return "The update manifest points to an untrusted download location. Try again later or install manually from GitHub.";

        return "Could not install the update. Lock the vault, check your connection, and try again.";
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex;
    }

    private static bool IsHostUnknown(Exception ex)
    {
        var text = ex.Message;
        return text.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Non-existent domain", StringComparison.OrdinalIgnoreCase);
    }

    private static string ForSocket(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
            "Could not reach the Fortiva update server. Check your internet connection and try again later.",
        SocketError.TimedOut =>
            "Update check timed out. Check your internet connection and try again.",
        _ => "Could not check for updates. Check your internet connection and try again later."
    };

    private static string ForHttp(HttpRequestException ex)
    {
        if (IsHostUnknown(ex))
            return "Could not reach the Fortiva update server. Check your internet connection and try again later.";
        return "Could not check for updates. Check your internet connection and try again later.";
    }
}
