using Fortiva.Core.BrowserBridge;

namespace Fortiva.Core.Tests.BrowserBridge;

public sealed class BridgeHostCircuitBreakerTests : IDisposable
{
    private readonly string _stateFile;

    public BridgeHostCircuitBreakerTests()
    {
        _stateFile = Path.Combine(Path.GetTempPath(), $"fortiva-cb-{Guid.NewGuid():N}.json");
        BridgeHostCircuitBreaker.ConfigureStateFileForTests(_stateFile);
    }

    public void Dispose()
    {
        BridgeHostCircuitBreaker.ConfigureStateFileForTests(null);
        if (File.Exists(_stateFile))
            File.Delete(_stateFile);
    }

    [Fact]
    public void GetBackoffMilliseconds_ReturnsZero_WhenFewExits()
    {
        BridgeHostCircuitBreaker.RecordExit(enterprise: false);
        BridgeHostCircuitBreaker.RecordExit(enterprise: false);
        Assert.Equal(0, BridgeHostCircuitBreaker.GetBackoffMilliseconds(enterprise: false));
    }

    [Fact]
    public void GetBackoffMilliseconds_ReturnsBackoff_AfterThreshold()
    {
        for (var i = 0; i < BridgeHostCircuitBreaker.MaxExitsInWindow; i++)
            BridgeHostCircuitBreaker.RecordExit(enterprise: false);

        Assert.Equal((int)BridgeHostCircuitBreaker.Backoff.TotalMilliseconds,
            BridgeHostCircuitBreaker.GetBackoffMilliseconds(enterprise: false));
    }
}
