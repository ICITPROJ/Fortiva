using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Browser-bridge side: launch Fortiva if needed, foreground it, and wait for unlock.</summary>
public static class BridgeUnlockClient
{
    private const int ConnectRetryCount = 60;
    private const int ConnectRetryDelayMs = 500;
    private const int PostLaunchWarmupMs = 2500;
    private const int UnlockPipeWaitMs = 35_000;

    /// <summary>Set when the last <see cref="RequestUnlockAsync"/> failed due to broker rate limiting.</summary>
    public static bool LastFailureWasRateLimited { get; private set; }

    public static async Task<bool> RequestUnlockAsync(int totalTimeoutMs = 120_000)
    {
        LastFailureWasRateLimited = false;

        var fastTest = string.Equals(Environment.GetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST"), "1", StringComparison.Ordinal);
        if (fastTest)
            totalTimeoutMs = 2000;

        using var cts = new CancellationTokenSource(totalTimeoutMs);

        var presence = await BridgePresenceClient.RequestStatusAsync(timeoutMs: 2000);
        if (BridgePresenceStatus.IsUnlocked(presence))
            return true;

        if (!BridgeProcessCheck.IsFortivaRunning())
        {
            if (!BridgeAppLauncher.TryLaunchFortiva())
                return false;

            await WaitForUnlockPipeAsync(cts.Token, UnlockPipeWaitMs);
        }
        else
        {
            BridgeAppLauncher.TryForegroundFortiva();
            await Task.Delay(fastTest ? 100 : PostLaunchWarmupMs, cts.Token);
        }

        var unlockSent = false;
        for (var attempt = 0; attempt < ConnectRetryCount && !cts.IsCancellationRequested; attempt++)
        {
            presence = await BridgePresenceClient.RequestStatusAsync(timeoutMs: 1500);
            if (BridgePresenceStatus.IsUnlocked(presence))
                return true;

            if (!unlockSent)
            {
                var unlockResult = await TrySendUnlockAsync(cts.Token);
                if (unlockResult == UnlockPipeResult.RateLimited)
                {
                    LastFailureWasRateLimited = true;
                    return false;
                }

                unlockSent = true;
                if (unlockResult == UnlockPipeResult.AlreadyUnlocked)
                    return true;
            }

            if (!BridgeProcessCheck.IsFortivaRunning() && attempt % 6 == 5)
                BridgeAppLauncher.TryLaunchFortiva();
            else if (attempt % 8 == 7)
                BridgeAppLauncher.TryForegroundFortiva();

            await Task.Delay(ConnectRetryDelayMs, cts.Token);
        }

        return false;
    }

    private enum UnlockPipeResult
    {
        Failed,
        Accepted,
        AlreadyUnlocked,
        RateLimited
    }

    private static string? ResolveUnlockPipeName()
        => BridgePipeNaming.TryUnlockPipeNameInProcess()
            ?? BridgePipeNaming.TryUnlockPipeName(BridgeNativeForwarder.IsEnterpriseEdition);

    private static async Task<bool> WaitForUnlockPipeAsync(CancellationToken ct, int maxWaitMs)
    {
        var deadline = Environment.TickCount64 + maxWaitMs;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var pipeName = ResolveUnlockPipeName();
                if (pipeName is null)
                {
                    await Task.Delay(350, ct);
                    continue;
                }

                using var client = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(400, ct);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                try { await Task.Delay(350, ct); } catch (OperationCanceledException) { throw; }
            }
        }

        return false;
    }

    private static async Task<UnlockPipeResult> TrySendUnlockAsync(CancellationToken ct)
    {
        try
        {
            var pipeName = ResolveUnlockPipeName();
            if (pipeName is null)
                return UnlockPipeResult.Failed;

            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000, ct);

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("UNLOCK".AsMemory(), ct);
            var response = (await reader.ReadLineAsync(ct))?.Trim();
            return response switch
            {
                "OK" => UnlockPipeResult.Accepted,
                "ALREADY_UNLOCKED" => UnlockPipeResult.AlreadyUnlocked,
                "RATE_LIMITED" => UnlockPipeResult.RateLimited,
                _ => UnlockPipeResult.Failed
            };
        }
        catch (OperationCanceledException) { throw; }
        catch { return UnlockPipeResult.Failed; }
    }
}

/// <summary>Finds Fortiva beside BrowserBridge, launches it, or brings an existing window forward.</summary>
public static class BridgeAppLauncher
{
    public static bool TryLaunchOrActivate() =>
        TryForegroundFortiva() || TryLaunchFortiva();

    public static bool TryLaunchFortiva()
    {
        if (BridgeProcessCheck.IsFortivaRunning())
            return true;

        var bridgeDir = AppContext.BaseDirectory;
        var installRoot = ResolveInstallRootFromBridgeDir(bridgeDir);
        if (installRoot is null || !BridgeClientValidator.IsTrustedInstallRoot(installRoot))
            return false;

        foreach (var name in new[] { "Fortiva.Personal.exe", "Fortiva.Enterprise.exe" })
        {
            var path = Path.Combine(installRoot, name);
            if (!File.Exists(path))
                continue;
            if (!BridgeClientValidator.IsAllowedExecutablePath(path, [installRoot]))
                continue;
            try
            {
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installRoot
                });
                return true;
            }
            catch { /* try next */ }
        }

        return false;
    }

    internal static string? ResolveInstallRootFromBridgeDir(string bridgeDir)
    {
        var hostPath = Path.Combine(bridgeDir, BridgeClientValidator.BridgeHostExecutableName);
        return BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(hostPath);
    }

    public static bool TryForegroundFortiva()
    {
        if (TryActivateRunning("Fortiva.Personal") || TryActivateRunning("Fortiva.Enterprise"))
            return true;

        return BridgeProcessCheck.IsFortivaRunning();
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
