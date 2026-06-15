using System.Runtime.CompilerServices;

namespace Fortiva.AppHost.Tests;

internal static class AppHostTestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
    }
}
