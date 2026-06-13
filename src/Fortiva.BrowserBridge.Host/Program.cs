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

/// <summary>
/// Chrome/Edge native messaging host: forwards requests to Fortiva named pipe server.
/// </summary>
var installRoot = inferredRoot;
if (installRoot is not null)
    BridgeClientValidator.ConfigureAllowedInstallRoots(installRoot);

if (!BridgePipeNaming.HasActiveSession(isEnterprise))
    Environment.Exit(0);

var integrityOk = NativeHostIntegrity.VerifyCurrentProcess(installRoot);
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var lengthBuf = new byte[4];
while (true)
{
    var read = await stdin.ReadAsync(lengthBuf.AsMemory(0, 4));
    if (read < 4) break;
    var len = BitConverter.ToInt32(lengthBuf, 0);
    if (len <= 0 || len > 1024 * 1024) break;
    var msgBuf = new byte[len];
    var offset = 0;
    while (offset < len)
        offset += await stdin.ReadAsync(msgBuf.AsMemory(offset, len - offset));
    var json = Encoding.UTF8.GetString(msgBuf);
    var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
    string response;
    if (!integrityOk)
    {
        response = BridgeJson.Serialize(new BridgeStatusResponse
        {
            Ok = false,
            Status = "setup_required",
            Message = "Bridge host failed integrity check. Reinstall Fortiva or run Connect browser in Settings."
        });
    }
    else
    {
        try
        {
            using var reqCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            response = await BridgeNativeForwarder.HandleAsync(doc.RootElement, reqCts.Token);
        }
        catch (OperationCanceledException)
        {
            response = BridgeJson.Serialize(new BridgeStatusResponse
            {
                Ok = false,
                Status = "bridge_warming",
                Message = "Fortiva is starting the bridge. Wait a moment, then click Fill again."
            });
        }
    }

    var respBytes = Encoding.UTF8.GetBytes(response);
    var header = BitConverter.GetBytes(respBytes.Length);
    await stdout.WriteAsync(header);
    await stdout.WriteAsync(respBytes);
    await stdout.FlushAsync();
}
