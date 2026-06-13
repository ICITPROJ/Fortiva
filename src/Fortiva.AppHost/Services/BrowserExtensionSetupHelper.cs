using System.Diagnostics;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.BrowserBridge;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Fortiva.AppHost.Services;

/// <summary>One-click browser extension setup for Settings, onboarding, and first-run prompts.</summary>
public static class BrowserExtensionSetupHelper
{
    public enum SupportedBrowser
    {
        Edge,
        Chrome
    }

    public enum ExtensionConnectMode
    {
        Manual,
        AutoLoaded,
        PolicyManaged
    }

    /// <summary>Live state for Settings and onboarding browser-extension panels.</summary>
    public enum BridgeFillReadiness
    {
        FilesMissing,
        SetupNeeded,
        PolicyManaged,
        VaultMissing,
        VaultLocked,
        BridgeStarting,
        Ready
    }

    public sealed record BrowserConnectResult(
        bool Success,
        string? ExtensionPath,
        SupportedBrowser Browser,
        ExtensionConnectMode Mode,
        string? Error)
    {
        public bool AutoLoadAttempted => Mode == ExtensionConnectMode.AutoLoaded;

        public static BrowserConnectResult Ok(string path, SupportedBrowser browser, ExtensionConnectMode mode)
            => new(true, path, browser, mode, null);

        public static BrowserConnectResult Fail(string? error)
            => new(false, null, SupportedBrowser.Edge, ExtensionConnectMode.Manual, error);
    }

    public static BrowserBridgeInstallStatus GetStatus(ShellViewModel vm)
        => BrowserBridgeInstallService.GetStatus(AppContext.BaseDirectory, vm.IsEnterprise);

    /// <summary>
    /// True when staged extension manifest version does not match the installed Fortiva app
    /// (user should Reload in edge://extensions or chrome://extensions).
    /// </summary>
    public static bool ExtensionVersionNeedsReload(ShellViewModel vm, out string? stagedVersion, out string? appVersion)
    {
        stagedVersion = null;
        appVersion = Fortiva.Core.Updates.AppVersion.Current;
        var staging = GetStagingPath(vm);
        var manifestPath = Path.Combine(staging, "manifest.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            stagedVersion = ExtensionIdHelper.ReadVersionFromManifestFile(manifestPath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(stagedVersion))
            return false;

        static string Normalize(string v)
        {
            var parts = v.Trim().Split('.');
            return parts.Length >= 3
                ? string.Join('.', parts.Take(3))
                : v.Trim();
        }

        return !string.Equals(Normalize(stagedVersion), Normalize(appVersion), StringComparison.Ordinal);
    }

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
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Extension folder not found: {path}");

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    public static Task OpenEdgeExtensionsAsync()
        => OpenBrowserExtensionsAsync(SupportedBrowser.Edge);

    /// <summary>
    /// Opens the browser extensions management page. Uses the browser executable directly because
    /// <c>Launcher.LaunchUriAsync</c> cannot open internal <c>edge://</c> / <c>chrome://</c> URLs.
    /// </summary>
    public static Task OpenBrowserExtensionsAsync(SupportedBrowser browser)
    {
        var extensionsUrl = browser == SupportedBrowser.Chrome
            ? "chrome://extensions/"
            : "edge://extensions/";

        var exe = ResolveBrowserExecutable(browser);
        if (exe is null)
            throw new InvalidOperationException($"{browser} was not found on this PC.");

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = extensionsUrl,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    public static void CopyPathToClipboard(string path)
    {
        var package = new DataPackage();
        package.SetText(path);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// One-click setup: register native messaging, then auto-load, policy-managed, or guided manual steps.
    /// </summary>
    public static async Task<BrowserConnectResult> ConnectBrowserAsync(ShellViewModel vm, XamlRoot xamlRoot)
    {
        var setup = EnsureReady(vm);
        if (!setup.Success)
            return BrowserConnectResult.Fail(setup.Error ?? "Browser setup failed.");

        try { BridgeHostProcessCleanup.StopOrphanedHosts(); } catch { /* best effort */ }
        if (vm.IsUnlocked)
        {
            try
            {
                if (vm.IsBridgeHealthy())
                    vm.EnsureBridgeInfrastructureHealthy();
                else
                    await vm.RestartBridgeInfrastructureAsync().ConfigureAwait(true);
            }
            catch { /* best effort — setup can still continue */ }
        }

        var path = setup.ExtensionStagingPath!;
        var browser = DetectPreferredBrowser();

        if (vm.IsEnterprise && BrowserExtensionPolicyService.IsForceInstallConfigured())
        {
            await ShowPolicyManagedWizardAsync(xamlRoot, browser);
            return BrowserConnectResult.Ok(path, browser, ExtensionConnectMode.PolicyManaged);
        }

        CopyPathToClipboard(path);

        var mode = ExtensionConnectMode.Manual;
        if (TryLaunchBrowserWithExtension(browser, path))
        {
            mode = ExtensionConnectMode.AutoLoaded;
        }
        else if (IsBrowserRunning(browser))
        {
            var choice = await PromptCloseBrowserForAutoInstallAsync(xamlRoot, browser);
            if (choice == CloseBrowserChoice.Cancel)
                return BrowserConnectResult.Fail("Browser setup cancelled.");

            if (choice == CloseBrowserChoice.CloseAndInstall && await TryCloseBrowserAsync(browser).ConfigureAwait(true))
            {
                await Task.Delay(2000).ConfigureAwait(true);
                if (TryLaunchBrowserWithExtension(browser, path))
                    mode = ExtensionConnectMode.AutoLoaded;
            }
        }

        if (mode != ExtensionConnectMode.AutoLoaded)
        {
            try { await OpenBrowserExtensionsAsync(browser); } catch { /* user can open manually */ }
            OpenExtensionFolder(path);
        }

        await ShowConnectWizardAsync(xamlRoot, path, browser, mode);
        return BrowserConnectResult.Ok(path, browser, mode);
    }

    public static async Task ShowFirstRunPromptAsync(XamlRoot xamlRoot, ShellViewModel vm)
    {
        if (vm.IsAdmin || vm.PersonalSettings.BrowserExtensionSetupDismissed)
            return;

        var dialog = new ContentDialog
        {
            Title = "Use Fortiva in your browser?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text =
                    "Fill saved logins with one click on any website.\n\n" +
                    "Setup takes about 30 seconds: Fortiva opens your browser and guides you through " +
                    "one-time extension loading. After that, click the Fortiva icon on login pages."
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

    public static BridgeFillReadiness GetFillReadiness(ShellViewModel vm)
    {
        var status = GetStatus(vm);
        if (!status.BridgeExecutableFound || status.ExtensionSourcePath is null)
            return BridgeFillReadiness.FilesMissing;
        if (!status.IsReadyForBrowser)
            return BridgeFillReadiness.SetupNeeded;
        if (status.ExtensionForceInstallConfigured)
            return BridgeFillReadiness.PolicyManaged;
        if (!vm.VaultExists)
            return BridgeFillReadiness.VaultMissing;
        if (!vm.IsUnlocked)
            return BridgeFillReadiness.VaultLocked;
        if (vm.IsBridgeHealthy())
            return BridgeFillReadiness.Ready;
        return BridgeFillReadiness.BridgeStarting;
    }

    public static (string Headline, string Detail, string IconGlyph) DescribeFillReadiness(BridgeFillReadiness readiness)
        => readiness switch
        {
            BridgeFillReadiness.Ready =>
                ("Ready to fill",
                    "On any login page, click the Fortiva icon in Edge or Chrome, then Fill. Fortiva can launch and unlock automatically.",
                    "\uE73E"),
            BridgeFillReadiness.VaultLocked =>
                ("Vault locked",
                    "Fortiva does not need to stay open. On a login page, click Fill — Fortiva will open and ask for Windows Hello or your master password.",
                    "\uE72E"),
            BridgeFillReadiness.BridgeStarting =>
                ("Bridge starting",
                    "The secure connection to your browser is still starting. Wait a few seconds or click Restart bridge below.",
                    "\uE895"),
            BridgeFillReadiness.SetupNeeded =>
                ("One-time setup",
                    "Click Connect browser below (~30 seconds). Then reload the extension in Edge or Chrome after Fortiva updates.",
                    "\uE8E5"),
            BridgeFillReadiness.PolicyManaged =>
                ("Managed by IT",
                    "Your organization installs the extension via policy. Restart Chrome or Edge if the Fortiva icon is missing.",
                    "\uE774"),
            BridgeFillReadiness.VaultMissing =>
                ("Create your vault first",
                    "Complete Fortiva setup, then return here to connect your browser.",
                    "\uE7BA"),
            _ =>
                ("Extension files missing",
                    "Reinstall Fortiva or run Connect browser after a repair install.",
                    "\uE946")
        };

    public static string FormatStatusMessage(BrowserBridgeInstallStatus status)
    {
        if (!status.BridgeExecutableFound || status.ExtensionSourcePath is null)
            return "Extension files missing — reinstall Fortiva.";

        if (!status.IsReadyForBrowser)
            return "One-time setup needed — click Connect browser below (~30 seconds).";

        if (status.ExtensionForceInstallConfigured)
            return "IT policy will install the browser extension. Restart Chrome or Edge if it is not visible yet.";

        return "Extension installed. Live status updates when the vault is unlocked.";
    }

    /// <summary>Install state plus live bridge health (requires Fortiva running).</summary>
    public static string FormatLiveStatusMessage(BrowserBridgeInstallStatus status, ShellViewModel vm)
        => DescribeFillReadiness(GetFillReadiness(vm)).Detail;

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

    public static Task<bool> TryCloseBrowserAsync(SupportedBrowser browser)
        => Task.Run(async () =>
        {
            var processName = browser == SupportedBrowser.Edge ? "msedge" : "chrome";
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                            process.CloseMainWindow();
                    }
                    catch
                    {
                        /* best effort */
                    }
                }

                await Task.Delay(1500).ConfigureAwait(false);
                if (!IsBrowserRunning(browser))
                    return true;

                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                }

                await Task.Delay(500).ConfigureAwait(false);
                return !IsBrowserRunning(browser);
            }
            catch
            {
                return false;
            }
        });

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

    private enum CloseBrowserChoice
    {
        CloseAndInstall,
        Manual,
        Cancel
    }

    private static async Task<CloseBrowserChoice> PromptCloseBrowserForAutoInstallAsync(
        XamlRoot xamlRoot,
        SupportedBrowser browser)
    {
        var browserName = browser == SupportedBrowser.Chrome ? "Chrome" : "Edge";
        var dialog = new ContentDialog
        {
            Title = $"{browserName} is already open",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text =
                    $"{browserName} is running, so Fortiva cannot install the extension automatically.\n\n" +
                    "Close the browser and let Fortiva reopen it with the extension loaded? " +
                    "Unsaved work in other tabs may be lost — Edge and Chrome usually restore tabs on restart.\n\n" +
                    "Or choose manual setup (Developer mode → Load unpacked) if you prefer."
            },
            PrimaryButtonText = $"Close {browserName} and install",
            SecondaryButtonText = "Set up manually instead",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => CloseBrowserChoice.CloseAndInstall,
            ContentDialogResult.Secondary => CloseBrowserChoice.Manual,
            _ => CloseBrowserChoice.Cancel
        };
    }

    private static async Task ShowPolicyManagedWizardAsync(XamlRoot xamlRoot, SupportedBrowser browser)
    {
        var browserName = browser == SupportedBrowser.Chrome ? "Chrome" : "Edge";
        var dialog = new ContentDialog
        {
            Title = "Browser extension managed by IT",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text =
                    "This PC is configured to install the Fortiva browser extension automatically " +
                    $"via organization policy.\n\n" +
                    $"1. Restart {browserName} if the Fortiva icon is not in the toolbar\n" +
                    "2. Open any login page\n" +
                    "3. Click the Fortiva icon, then Fill\n\n" +
                    "On any login page, click the Fortiva icon → Fill — Fortiva will open if needed and ask you to unlock."
            },
            PrimaryButtonText = $"Open {browserName} extensions",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try { await OpenBrowserExtensionsAsync(browser); } catch { /* optional */ }
        }
    }

    private static async Task ShowConnectWizardAsync(
        XamlRoot xamlRoot,
        string extensionPath,
        SupportedBrowser browser,
        ExtensionConnectMode mode)
    {
        var browserName = browser == SupportedBrowser.Chrome ? "Chrome" : "Edge";
        var steps = mode switch
        {
            ExtensionConnectMode.AutoLoaded =>
                $"✓ Fortiva opened {browserName} with the extension loaded.\n\n" +
                "Try it now:\n" +
                "1. Open any login page\n" +
                "2. Click the Fortiva icon in the toolbar\n" +
                "3. Click Fill\n\n" +
                "On any login page, click the Fortiva icon → Fill — Fortiva will open if needed and ask you to unlock.",
            ExtensionConnectMode.PolicyManaged =>
                "IT policy will install the extension. Restart your browser if needed, then use the Fortiva toolbar icon on login pages.",
            _ =>
                $"Almost done — finish in {browserName}:\n\n" +
                "1. Turn on Developer mode (top-right)\n" +
                "2. Click Load unpacked\n" +
                "3. Select the folder that opened (path is on your clipboard)\n\n" +
                "Tip: choose the **extension** subfolder (contains manifest.json), not the parent Fortiva folder.\n\n" +
                "Then open a login page and click the Fortiva toolbar icon."
        };

        var dialog = new ContentDialog
        {
            Title = mode == ExtensionConnectMode.AutoLoaded ? "Extension loaded" : "Finish in your browser",
            Content = new TextBlock
            {
                Text = steps,
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = mode == ExtensionConnectMode.AutoLoaded
                ? "Got it"
                : $"Open {browserName} extensions",
            SecondaryButtonText = "Open extension folder",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        FortivaDialogs.Configure(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && mode == ExtensionConnectMode.Manual)
        {
            try { await OpenBrowserExtensionsAsync(browser); } catch { /* optional */ }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            OpenExtensionFolder(extensionPath);
        }
    }
}
