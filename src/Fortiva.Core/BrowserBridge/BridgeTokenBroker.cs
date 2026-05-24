using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Serves the per-unlock bridge session token over a secured named pipe.
/// Token is held in process memory only — not written to disk.
/// </summary>
public sealed class BridgeTokenBroker : IDisposable
{
    public const string PipeName = "Fortiva.Bridge.Token";

    private readonly string _sessionToken;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public BridgeTokenBroker(string sessionToken)
    {
        _sessionToken = sessionToken;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var server = CreateSecuredServerStream();
            try
            {
                await server.WaitForConnectionAsync(ct);
                if (!BridgePipeGuard.IsAllowedClient(server))
                    continue;
                await HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* continue listening */ }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(ct);
        if (line is null || !string.Equals(line.Trim(), "GET", StringComparison.OrdinalIgnoreCase))
            return;
        await writer.WriteLineAsync(_sessionToken.AsMemory(), ct);
    }

    private static NamedPipeServerStream CreateSecuredServerStream()
    {
        var pipeSecurity = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve current user SID for pipe ACL.");
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
