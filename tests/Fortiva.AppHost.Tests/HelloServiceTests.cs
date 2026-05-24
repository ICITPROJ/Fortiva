using Fortiva.AppHost.Services;
using Windows.Security.Credentials.UI;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class HelloServiceTests
{
    [Fact]
    public void DescribeAvailability_ReturnsActionableMessageForNotConfigured()
    {
        var message = HelloService.DescribeAvailability(UserConsentVerifierAvailability.NotConfiguredForUser);
        Assert.Contains("Sign-in options", message);
    }

    [Fact]
    public void MapResult_CanceledIsNotVerified()
    {
        var result = HelloService.MapResult(UserConsentVerificationResult.Canceled);
        Assert.False(result.Verified);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
}
