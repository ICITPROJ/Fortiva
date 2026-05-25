using System.Diagnostics;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.BrowserBridge;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Fortiva.AppHost.Services;

/// <summary>Shared browser-extension setup actions for Settings, onboarding, and first-run prompts.</summary>
public static class BrowserExtensionSetupHelper
{
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
        => await Launcher.LaunchUriAsync(new Uri("microsoft-edge://extensions"));

    public static void CopyPathToClipboard(string path)
    {
        var package = new DataPackage();
        package.SetText(path);
        Clipboard.SetContent(package);
    }

    public static async Task ShowFirstRunPromptAsync(XamlRoot xamlRoot, ShellViewModel vm)
    {
        if (vm.IsAdmin || vm.IsEnterprise || vm.PersonalSettings.BrowserExtensionSetupDismissed)
            return;

        EnsureReady(vm);
        var path = GetStagingPath(vm);

        var content = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text =
                "Fortiva can fill logins in Edge from your unlocked vault.\n\n" +
                "One-time setup:\n" +
                "1. Click Open Edge extensions below\n" +
                "2. Turn on Developer mode\n" +
                "3. Load unpacked → select:\n" +
                path +
                "\n\nYou can change this anytime in Settings → Browser extension."
        };

        var dialog = new ContentDialog
        {
            Title = "Connect your browser",
            Content = content,
            PrimaryButtonText = "Open Edge extensions",
            SecondaryButtonText = "Open extension folder",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try { await OpenEdgeExtensionsAsync(); } catch { /* user can open manually */ }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            try
            {
                var setup = EnsureStagingFolder(vm);
                if (setup.Success)
                    OpenExtensionFolder(setup.ExtensionStagingPath!);
            }
            catch { /* optional */ }
        }

        if (result != ContentDialogResult.None)
            vm.SetBrowserExtensionSetupDismissed();
    }
}
