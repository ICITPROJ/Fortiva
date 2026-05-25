using Fortiva.Core.Policy;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Clipboard operations via WinRT DataPackage. Clears clipboard after timeout.
/// All Clipboard calls (Set and Clear) must run on the UI thread.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private FortivaPolicy? _policy;
    private int _personalClearSeconds;
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _clearCts;

    private readonly Action<string>? _onPolicyViolation;

    public ClipboardService(FortivaPolicy? policy = null, int personalClearSeconds = 30, Action<string>? onPolicyViolation = null)
    {
        _policy = policy;
        _personalClearSeconds = personalClearSeconds;
        _onPolicyViolation = onPolicyViolation;
        _ui = DispatcherQueue.GetForCurrentThread()
              ?? throw new InvalidOperationException("ClipboardService must be created on the UI thread.");
    }

    public bool IsAllowed => PolicyEnforcer.IsClipboardAllowed(_policy);

    public void RefreshPolicy(FortivaPolicy? policy, int personalClearSeconds)
    {
        _policy = policy;
        _personalClearSeconds = personalClearSeconds;
    }

    public static event Action<int>? ClipboardCopied;

    public void CopyText(string text)
    {
        if (!IsAllowed)
        {
            _onPolicyViolation?.Invoke("Clipboard copy blocked by policy");
            throw new InvalidOperationException("Clipboard is disabled by policy.");
        }
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        var seconds = PolicyEnforcer.GetClipboardClearSeconds(_policy, personalDefault: _personalClearSeconds);
        if (seconds > 0)
            ClipboardCopied?.Invoke(seconds);
        ScheduleClear();
    }

    public void CopyPassword(string password) => CopyText(password);

    public void ClearNow()
    {
        _clearCts?.Cancel();
        // Clipboard.Clear() is a COM STA call — must be on the UI thread
        _ui.TryEnqueue(() =>
        {
            try { Clipboard.Clear(); } catch { /* best effort */ }
        });
    }

    private void ScheduleClear()
    {
        _clearCts?.Cancel();
        _clearCts = new CancellationTokenSource();
        var seconds = PolicyEnforcer.GetClipboardClearSeconds(_policy, personalDefault: _personalClearSeconds);
        if (seconds <= 0) return;

        var token = _clearCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                ClearNow();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void Dispose()
    {
        _clearCts?.Cancel();
        _clearCts?.Dispose();
    }
}
