using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.Platform;

public sealed class AuthenticodeVerifierTests
{
    [Fact]
    public void IsSigned_skips_verification_when_policy_disabled()
    {
        var previous = AuthenticodePolicy.RequireSignedExecutables;
        try
        {
            AuthenticodePolicy.RequireSignedExecutables = false;
            var path = Path.Combine(Path.GetTempPath(), "fortiva-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllText(path, "");
                Assert.True(AuthenticodeVerifier.IsSigned(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
        finally
        {
            AuthenticodePolicy.RequireSignedExecutables = previous;
        }
    }

    [Fact]
    public void ConfigureForEdition_require_codesign_blocked_when_allow_unsigned_set()
    {
        var previous = AuthenticodePolicy.RequireSignedExecutables;
        var priorRequire = Environment.GetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN");
        var priorAllow = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", "1");
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
            AuthenticodePolicy.ConfigureForEdition("Enterprise");
            Assert.False(AuthenticodePolicy.RequireSignedExecutables);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", priorRequire);
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorAllow);
            AuthenticodePolicy.RequireSignedExecutables = previous;
        }
    }

    [Fact]
    public void ConfigureForEdition_leaves_personal_unsigned_by_default()
    {
        var previous = AuthenticodePolicy.RequireSignedExecutables;
        var priorRequire = Environment.GetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN");
        var priorAllow = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", null);
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            AuthenticodePolicy.ConfigureForEdition("Personal");
            Assert.False(AuthenticodePolicy.RequireSignedExecutables);

            AuthenticodePolicy.ConfigureForEdition("Enterprise");
            Assert.False(AuthenticodePolicy.RequireSignedExecutables);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", priorRequire);
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorAllow);
            AuthenticodePolicy.RequireSignedExecutables = previous;
        }
    }

    [Fact]
    public void ConfigureForEdition_enables_when_require_codesign_set_in_release()
    {
        var previous = AuthenticodePolicy.RequireSignedExecutables;
        var priorRequire = Environment.GetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN");
        var priorAllow = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", "1");
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            AuthenticodePolicy.ConfigureForEdition("Enterprise");
#if DEBUG
            Assert.False(AuthenticodePolicy.RequireSignedExecutables);
#else
            Assert.True(AuthenticodePolicy.RequireSignedExecutables);
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_REQUIRE_CODESIGN", priorRequire);
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorAllow);
            AuthenticodePolicy.RequireSignedExecutables = previous;
        }
    }

    [Fact]
    public void AllowUnsignedBridgeForDevelopment_IsFalseInRelease()
    {
        if (!OperatingSystem.IsWindows())
            return;

#if DEBUG
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorBridge);
        }
#else
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        var priorActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());

            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
            Assert.False(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());

            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
            Assert.True(AuthenticodeVerifier.AllowUnsignedBridgeForDevelopment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", priorBridge);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", priorActions);
        }
#endif
    }

    [Fact]
    public void IsSigned_MissingFile_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "fortiva-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        var priorPolicy = AuthenticodePolicy.RequireSignedExecutables;
        var priorBridge = Environment.GetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE");
        var priorActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try
        {
            AuthenticodePolicy.RequireSignedExecutables = true;
            Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
            Assert.False(AuthenticodeVerifier.IsSigned(path));
        }
        finally
        {
            AuthenticodePolicy.RequireSignedExecutables = priorPolicy;
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", priorActions);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
