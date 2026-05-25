using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Licensing;
using Fortiva.Core.Platform;

namespace Fortiva.AppHost;

public partial class App : Application
{
    public static string Edition { get; private set; } = "Personal";
    public static IntPtr MainWindowHandle { get; private set; }

    private static DispatcherQueue? _uiDispatcher;
    private Window? _window;

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
        ProcessMitigation.EnableBestEffort();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException("App_UnhandledException", e.Exception);
        e.Handled = false;
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("CurrentDomain_UnhandledException", ex);
    }

    internal static void LogException(string context, Exception ex)
    {
        try
        {
            var folder = Edition switch
            {
                "Enterprise" => "FortivaEnterprise",
                "Admin"      => "FortivaAdmin",
                _            => "FortivaPersonal"
            };
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                folder, "fortiva-crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] {context}\n{ex}\n\n");
        }
        catch { }
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

            _window = new MainWindow();
            MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Activate();

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
