namespace Fortiva.Core.Services;

/// <summary>
/// Fires LockRequested after <see cref="TimeoutSeconds"/> of inactivity.
/// Call <see cref="ResetActivity"/> on any user interaction.
/// Respects the policy maximum via the timeout value passed by the host.
/// </summary>
public sealed class AutoLockTimer : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private int _timeoutSeconds;
    private DateTimeOffset _lastActivity;
    private bool _disposed;
    private int _lockSignaled;

    public event Action? LockRequested;

    public AutoLockTimer(int timeoutSeconds)
    {
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 300;
        _lastActivity = DateTimeOffset.UtcNow;
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(5, _timeoutSeconds / 10)));
        _timer = new System.Threading.Timer(Check, null, pollInterval, pollInterval);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = value > 0 ? value : 300;
    }

    public void ResetActivity()
    {
        _lastActivity = DateTimeOffset.UtcNow;
        Interlocked.Exchange(ref _lockSignaled, 0);
    }

    private void Check(object? _)
    {
        if (_disposed) return;
        if ((DateTimeOffset.UtcNow - _lastActivity).TotalSeconds >= _timeoutSeconds)
        {
            if (Interlocked.Exchange(ref _lockSignaled, 1) == 0)
                LockRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
