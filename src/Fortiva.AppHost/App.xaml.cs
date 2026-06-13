using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Licensing;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost;

public partial class App : Application
{
    public static string Edition { get; private set; } = "Personal";
    public static IntPtr MainWindowHandle { get; private set; }

    private static DispatcherQueue? _uiDispatcher;
    private static Window? _window;

    internal static void RegisterUiDispatcher(DispatcherQueue dispatcher) => _uiDispatcher = dispatcher;

    internal static void ExitForUpdate()
    {
        void Exit() => ((App)Current).Exit();
        if (_uiDispatcher?.TryEnqueue(Exit) != true)
            Exit();
    }

    public App()
    {
        Edition = ResolveEdition();
        AuthenticodePolicy.ConfigureForEdition(Edition);
        ProcessMitigation.EnableBestEffort();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException("App_UnhandledException", e.Exception);
        // Tell the user what happened (and where the log is) before the process terminates, instead
        // of the window silently disappearing. We still let the app terminate afterwards because the
        // managed state may be corrupt — a password manager should not soldier on in an unknown state.
        ShowFatalErrorDialog(e.Exception);
        e.Handled = false;
    }

    private static void ShowFatalErrorDialog(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CrashLogFolderName, "fortiva-crash.log");
            var message =
                "Fortiva ran into an unexpected error and needs to close.\n\n"
                + DescribeException(ex)
                + "\n\nYour vault is encrypted on disk and is not affected. "
                + "Details were saved to:\n" + logPath;
            MessageBoxW(MainWindowHandle, message, "Fortiva", MB_OK | MB_ICONERROR | MB_TOPMOST);
        }
        catch
        {
            /* never let the error reporter throw */
        }
    }

    private static string CrashLogFolderName => Edition switch
    {
        "Enterprise" => "FortivaEnterprise",
        "Admin" => "FortivaAdmin",
        _ => "FortivaPersonal"
    };

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_TOPMOST = 0x40000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("CurrentDomain_UnhandledException", ex);
    }

    internal static void LogException(string context, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CrashLogFolderName, "fortiva-crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {context}\n{ex}\n\n");
        }
        catch { }
    }

    /// <summary>Refreshes the cached HWND so WinRT UI (Hello, pickers) parents to the live window.</summary>
    public static IntPtr EnsureMainWindowHandle()
    {
        try
        {
            if (_window is not null)
                MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        }
        catch (Exception ex)
        {
            LogException("EnsureMainWindowHandle", ex);
        }

        return MainWindowHandle;
    }

    internal static string DescribeException(Exception ex)
    {
        if (ex is AggregateException aggregate)
        {
            ex = aggregate.Flatten().InnerException ?? aggregate;
        }

        if (!string.IsNullOrWhiteSpace(ex.Message))
            return ex.Message.Trim();

        if (ex is COMException com && com.HResult != 0)
            return $"COM error 0x{com.HResult:X8}";

        if (ex.InnerException is not null)
            return DescribeException(ex.InnerException);

        return ex.GetType().Name;
    }

    private static string ResolveEdition()
    {
        var name = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "";
        return name switch
        {
            "Fortiva.Enterprise" => "Enterprise",
            "Fortiva.Admin" => "Admin",
            _ => "Personal"
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (Edition is "Enterprise" or "Admin")
                LicenseVerifier.EnsureProductionKeyForEnterpriseBuild();

            if (Edition == "Admin" && !AdminElevation.IsRunningAsAdministrator())
            {
                LogException("OnLaunched", new UnauthorizedAccessException(
                    "Fortiva Admin Console must be run as Administrator."));
                throw new UnauthorizedAccessException(
                    "Fortiva Admin Console requires Administrator privileges. " +
                    "Right-click the app and choose Run as administrator.");
            }

            ThemeService.ApplyApplicationThemeEarly(ShellViewModel.Current.ThemePreference);

            if (Edition is "Personal" or "Enterprise")
            {
                var installRoot = AppContext.BaseDirectory;
                var enterprise = Edition == "Enterprise";
                BridgeClientValidator.ConfigureAllowedInstallRoots(installRoot);
                ShellViewModel.Current.StartBridgeUnlockListener(installRoot);
                try
                {
                    BrowserBridgeInstallService.RepairNativeHostIfStale(installRoot, enterprise);
                }
                catch (Exception ex)
                {
                    LogException("RepairNativeHostIfStale", ex);
                }
            }

            _window = new MainWindow();
            MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Activated += (_, _) => EnsureMainWindowHandle();
            _window.Activate();
            EnsureMainWindowHandle();

            if (Edition == "Personal")
                _ = UpdateService.Current.TryAutoUpdateOnLaunchAsync();
        }
        catch (Exception ex)
        {
            LogException("OnLaunched", ex);
            throw;
        }
    }
}
