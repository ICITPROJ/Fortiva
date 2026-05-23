using Fortiva.Core.Services;

namespace Fortiva.Core.Tests.Vault;

public class AutoLockTimerTests
{
    [Fact]
    public async Task AutoLock_FiresAfterTimeout()
    {
        var fired = false;
        using var timer = new AutoLockTimer(timeoutSeconds: 2);
        timer.LockRequested += () => fired = true;
        await Task.Delay(3000);
        Assert.True(fired, "Auto-lock did not fire within 3 s.");
    }

    [Fact]
    public async Task AutoLock_DoesNotFire_AfterReset()
    {
        var fired = false;
        using var timer = new AutoLockTimer(timeoutSeconds: 2);
        timer.LockRequested += () => fired = true;
        await Task.Delay(1500);
        timer.ResetActivity();
        await Task.Delay(1500);
        Assert.False(fired, "Auto-lock fired despite activity reset.");
    }

    [Fact]
    public void AutoLock_Dispose_DoesNotThrow()
    {
        var timer = new AutoLockTimer(timeoutSeconds: 60);
        var ex = Record.Exception(timer.Dispose);
        Assert.Null(ex);
    }
}
