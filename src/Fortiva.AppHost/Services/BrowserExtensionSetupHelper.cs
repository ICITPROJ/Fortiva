using System.Diagnostics;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.BrowserBridge;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Fortiva.AppHost.Services;

/// <summary>One-click browser extension setup for Settings, onboarding, and first-run prompts.</summary>
public static class BrowserExtensionSetupHelper
{
    public enum SupportedBrowser
    {
        Edge,
        Chrome
    }

    public sealed record BrowserConnectResult(
        bool Success,
        string? ExtensionPath,
        SupportedBrowser Browser,
        bool AutoLoadAttempted,
        string? Error)
    {
        public static BrowserConnectResult Ok(string path, SupportedBrowser browser, bool autoLoadAttempted)
            => new(true, path, browser, autoLoadAttempted, null);

        public static BrowserConnectResult Fail(string? error)
            => new(false, null, SupportedBrowser.Edge, false, error);
    }

    public static BrowserBridgeInstallStatus GetStatus(ShellViewModel vm)
        => BrowserBridgeInstallService.GetStatus(AppContext.BaseDirectory, vm.IsEnterprise);

    public static BrowserBridgeInstallResult EnsureReady(ShellViewModel vm)
        => BrowserBridgeInstallService.EnsureInstalled(AppContext.BaseDirectory, vm.IsEnterprise);

    public static string GetStagingPath(ShellViewModel vm)
        => BrowserBridgeInstallService.GetExtensionStagingPath(vm.IsEnterprise);

    public static BrowserBridgeInstallResult EnsureStagingFolder(ShellViewModel vm)
    {
        var path = GetStagingPath(vm);
        if (Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.json")))
        {
            var status = GetStatus(vm);
            return BrowserBridgeInstallResult.Ok(
                path,
                status.BridgeExecutablePath ?? "",
                status.ExtensionId ?? "",
                status.HostName,
                status.NativeMessagingManifestPath);
        }

        return EnsureReady(vm);
    }

    public static void OpenExtensionFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public static async Task OpenEdgeExtensionsAsync()
        => await OpenBrowserExtensionsAsync(SupportedBrowser.Edge);

    public static async Task OpenBrowserExtensionsAsync(SupportedBrowser browser)
    {
        var uri = browser == SupportedBrowser.Chrome
            ? "chrome://extensions/"
            : "microsoft-edge://extensions";
        await Launcher.LaunchUriAsync(new Uri(uri));
    }

    public static void CopyPathToClipboard(string path)
    {
        var package = new DataPackage();
        package.SetText(path);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// One-click setup: register native messaging, copy path, open browser + folder, guide the user.
    /// </summary>
    public static async Task<BrowserConnectResult> ConnectBrowserAsync(ShellViewModel vm, XamlRoot xamlRoot)
    {
        var setup = EnsureReady(vm);
        if (!setup.Success)
            return BrowserConnectResult.Fail(setup.Error ?? "Browser setup failed.");

        var path = setup.ExtensionStagingPath!;
        CopyPathToClipboard(path);

        var browser = DetectPreferredBrowser();
        var autoLoadAttempted = TryLaunchBrowserWithExtension(browser, path);

        if (!autoLoadAttempted)
        {
            try { await OpenBrowserExtensionsAsync(browser); } catch { /* user can open manually */ }
            OpenExtensionFolder(path);
        }

        await ShowConnectWizardAsync(xamlRoot, path, browser, autoLoadAttempted);
        return BrowserConnectResult.Ok(path, browser, autoLoadAttempted);
    }

    public static async Task ShowFirstRunPromptAsync(XamlRoot xamlRoot, ShellViewModel vm)
    {
        if (vm.IsAdmin || vm.PersonalSettings.BrowserExtensionSetupDismissed)
            return;

        var dialog = new ContentDialog
        {
            Title = "Connect your browser?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text =
                    "Fill logins from Fortiva with one click on any website.\n\n" +
                    "Fortiva will prepare everything automatically. You only confirm " +
                    "Load unpacked once in your browser (about 30 seconds)."
            },
            PrimaryButtonText = "Connect browser",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ConnectBrowserAsync(vm, xamlRoot);
        vm.SetBrowserExtensionSetupDismissed();
    }

    public static string FormatStatusMessage(BrowserBridgeInstallStatus status)
    {
        if (!status.BridgeExecutableFound || status.ExtensionSourcePath is null)
            return "Extension files were not found with this install. Reinstall Fortiva.";

        if (!status.IsReadyForBrowser)
            return "Almost ready — click Connect browser to finish one-time setup.";

        return "Fortiva is linked to your browser. Load the extension once (Connect browser) if you have not already.";
    }

    public static SupportedBrowser DetectPreferredBrowser()
    {
        var progId = GetDefaultBrowserProgId();
        if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            && ResolveBrowserExecutable(SupportedBrowser.Chrome) is not null)
            return SupportedBrowser.Chrome;

        if (progId.Contains("Edge", StringComparison.OrdinalIgnoreCase)
            && ResolveBrowserExecutable(SupportedBrowser.Edge) is not null)
            return SupportedBrowser.Edge;

        if (ResolveBrowserExecutable(SupportedBrowser.Edge) is not null)
            return SupportedBrowser.Edge;

        if (ResolveBrowserExecutable(SupportedBrowser.Chrome) is not null)
            return SupportedBrowser.Chrome;

        return SupportedBrowser.Edge;
    }

    public static bool IsBrowserRunning(SupportedBrowser browser)
    {
        var processName = browser == SupportedBrowser.Edge ? "msedge" : "chrome";
        return Process.GetProcessesByName(processName).Length > 0;
    }

    public static bool TryLaunchBrowserWithExtension(SupportedBrowser browser, string extensionPath)
    {
        if (IsBrowserRunning(browser))
            return false;

        var exe = ResolveBrowserExecutable(browser);
        if (exe is null || !Directory.Exists(extensionPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--load-extension=\"{extensionPath}\"",
                UseShellExecute = false
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string? ResolveBrowserExecutable(SupportedBrowser browser)
    {
        var exeName = browser == SupportedBrowser.Edge ? "msedge.exe" : "chrome.exe";
        foreach (var candidate in BrowserExecutableCandidates(exeName))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> BrowserExecutableCandidates(string exeName)
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.Equals(exeName, "msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", exeName);
            yield return Path.Combine(programFiles, "Microsoft", "Edge", "Application", exeName);
        }
        else
        {
            yield return Path.Combine(programFiles, "Google", "Chrome", "Application", exeName);
            yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", exeName);
            yield return Path.Combine(localAppData, "Google", "Chrome", "Application", exeName);
        }
    }

    private static string GetDefaultBrowserProgId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            return key?.GetValue("ProgId") as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task ShowConnectWizardAsync(
        XamlRoot xamlRoot,
        string extensionPath,
        SupportedBrowser browser,
        bool autoLoaded)
    {
        var browserName = browser == SupportedBrowser.Chrome ? "Chrome" : "Edge";
        var steps = autoLoaded
            ? $"Fortiva opened {browserName} with the extension loaded.\n\n" +
              "If you see the Fortiva icon in the toolbar, you are done.\n\n" +
              "Otherwise: Developer mode ON → Load unpacked → select the folder (path copied to clipboard):\n" +
              extensionPath
            : $"{browserName} extensions and the extension folder are open.\n\n" +
              "Two quick steps:\n" +
              "1. Turn on Developer mode\n" +
              "2. Click Load unpacked and select the folder that opened\n\n" +
              "The folder path is also on your clipboard.";

        var dialog = new ContentDialog
        {
            Title = autoLoaded ? "Extension loaded" : "Finish in your browser",
            Content = new TextBlock
            {
                Text = steps,
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = autoLoaded ? "Got it" : $"Open {browserName} extensions again",
            SecondaryButtonText = "Open extension folder",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !autoLoaded)
        {
            try { await OpenBrowserExtensionsAsync(browser); } catch { /* optional */ }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            OpenExtensionFolder(extensionPath);
        }
    }
}
