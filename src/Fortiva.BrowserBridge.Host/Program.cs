using System.Text;
using System.Text.Json;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

var hostExePath = Environment.ProcessPath;
var inferredRoot = BridgeClientValidator.TryInferInstallRootFromBridgeHostPath(hostExePath);
var edition = inferredRoot?.Contains("Fortiva Enterprise", StringComparison.OrdinalIgnoreCase) == true
    ? "Enterprise"
    : "Personal";
var isEnterprise = edition == "Enterprise";
AuthenticodePolicy.ConfigureForEdition(edition);
BridgeNativeForwarder.ConfigureEdition(isEnterprise);

var installRoot = inferredRoot;
if (installRoot is not null)
    BridgeClientValidator.ConfigureAllowedInstallRoots(installRoot);

var backoffMs = BridgeHostCircuitBreaker.GetBackoffMilliseconds(isEnterprise);
if (backoffMs > 0)
    Thread.Sleep(backoffMs);

try
{
    if (!BridgePipeNaming.HasActiveSession(isEnterprise))
        return;

    var integrityOk = NativeHostIntegrity.VerifyCurrentProcess(installRoot);

    await using var pump = new NativeMessagingHostPump(isEnterprise, integrityOk);
    await pump.RunAsync();
}
finally
{
    BridgeHostCircuitBreaker.RecordExit(isEnterprise);
}
