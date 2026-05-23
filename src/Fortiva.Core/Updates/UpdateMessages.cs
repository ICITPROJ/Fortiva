using System.Net.Http;
using System.Net.Sockets;

namespace Fortiva.Core.Updates;

public static class UpdateMessages
{
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
