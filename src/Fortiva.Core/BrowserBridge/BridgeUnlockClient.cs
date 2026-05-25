using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Browser-bridge side: ask Fortiva to unlock, or launch / foreground the desktop app.</summary>
public static class BridgeUnlockClient
{
    private const int ConnectRetryCount = 12;
    private const int ConnectRetryDelayMs = 500;

    public static async Task<bool> RequestUnlockAsync(int totalTimeoutMs = 120_000)
    {
        using var cts = new CancellationTokenSource(totalTimeoutMs);

        if (!await TrySendUnlockAsync(cts.Token))
        {
            BridgeAppLauncher.TryLaunchOrActivate();
            await Task.Delay(800, cts.Token);
        }

        for (var attempt = 0; attempt < ConnectRetryCount && !cts.Token.IsCancellationRequested; attempt++)
        {
            if (await TrySendUnlockAsync(cts.Token))
                return true;
            await Task.Delay(ConnectRetryDelayMs, cts.Token);
        }

        return false;
    }

    private static async Task<bool> TrySendUnlockAsync(CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", BridgeUnlockBroker.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000, ct);

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("UNLOCK".AsMemory(), ct);
            var response = (await reader.ReadLineAsync(ct))?.Trim();
            return response is "OK" or "ALREADY_UNLOCKED";
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
}

/// <summary>Finds Fortiva beside BrowserBridge or running in memory and brings it forward.</summary>
public static class BridgeAppLauncher
{
    public static void TryLaunchOrActivate()
    {
        if (TryActivateRunning("Fortiva.Personal") || TryActivateRunning("Fortiva.Enterprise"))
            return;

        var bridgeDir = AppContext.BaseDirectory;
        foreach (var relative in new[] { "..\\Fortiva.Personal.exe", "..\\Fortiva.Enterprise.exe" })
        {
            var path = Path.GetFullPath(Path.Combine(bridgeDir, relative));
            if (!File.Exists(path))
                continue;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            catch { /* try next */ }
        }
    }

    private static bool TryActivateRunning(string processName)
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch { return false; }

        foreach (var proc in procs)
        {
            try
            {
                var hwnd = proc.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                    continue;
                ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
                return true;
            }
            catch { /* next process */ }
            finally { proc.Dispose(); }
        }

        return false;
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
