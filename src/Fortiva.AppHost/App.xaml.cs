using Microsoft.UI.Xaml;
using System;
using System.IO;
using Fortiva.Core.Licensing;
using Fortiva.Core.Platform;
using Fortiva.AppHost.Services;

namespace Fortiva.AppHost;

public partial class App : Application
{
    public static string Edition { get; private set; } = "Personal";
    public static IntPtr MainWindowHandle { get; private set; }

    private Window? _window;

    public App()
    {
        Edition = ResolveEdition();
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
