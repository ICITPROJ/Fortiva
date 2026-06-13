using System.Text;
using System.Text.Json;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Long-lived native messaging host: stdin requests + WinUI event-pipe push stream on stdout.
/// </summary>
public sealed class NativeMessagingHostPump : IAsyncDisposable
{
    private readonly bool _enterprise;
    private readonly bool _integrityOk;
    private readonly Stream _stdin;
    private readonly Stream _stdout;
    private readonly object _stdoutLock = new();
    private readonly CancellationTokenSource _cts = new();

    public NativeMessagingHostPump(bool enterprise, bool integrityOk, Stream? stdin = null, Stream? stdout = null)
    {
        _enterprise = enterprise;
        _integrityOk = integrityOk;
        _stdin = stdin ?? Console.OpenStandardInput();
        _stdout = stdout ?? Console.OpenStandardOutput();
    }

    public Task RunAsync() =>
        Task.WhenAll(
            ProcessBrowserStdinLoopAsync(_cts.Token),
            ListenToWinUiPushAsync(_cts.Token));

    private async Task ListenToWinUiPushAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipeName = BridgePipeNaming.TryEventPipeName(_enterprise);
            if (pipeName is null)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(
                    ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                await pipeClient.ConnectAsync(5000, ct).ConfigureAwait(false);

                using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);
                while (!ct.IsCancellationRequested && pipeClient.IsConnected)
                {
                    var line = await BridgeJson.ReadBoundedLineAsync(reader, ct).ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (!string.IsNullOrWhiteSpace(line))
                        WriteMessageToStdout(line);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task ProcessBrowserStdinLoopAsync(CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        while (!ct.IsCancellationRequested)
        {
            var read = await _stdin.ReadAsync(lengthBuf.AsMemory(0, 4), ct).ConfigureAwait(false);
            if (read < 4)
                break;

            var len = BitConverter.ToInt32(lengthBuf, 0);
            if (len <= 0 || len > 1024 * 1024)
                break;

            var msgBuf = new byte[len];
            var offset = 0;
            while (offset < len)
            {
                var chunk = await _stdin.ReadAsync(msgBuf.AsMemory(offset, len - offset), ct).ConfigureAwait(false);
                if (chunk == 0)
                    break;
                offset += chunk;
            }

            if (offset < len)
                break;

            var json = Encoding.UTF8.GetString(msgBuf);
            string response;
            if (!_integrityOk)
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
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                    var command = doc.RootElement.TryGetProperty("command", out var cmd)
                        ? cmd.GetString()
                        : null;

                    if (string.Equals(command, "ping", StringComparison.OrdinalIgnoreCase))
                    {
                        response = await HandlePingCommandAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        reqCts.CancelAfter(TimeSpan.FromSeconds(8));
                        response = await BridgeNativeForwarder.HandleAsync(doc.RootElement, reqCts.Token)
                            .ConfigureAwait(false);
                    }
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
                catch
                {
                    response = BridgeJson.Serialize(new BridgeStatusResponse
                    {
                        Ok = false,
                        Status = "setup_required",
                        Message = "Invalid request from extension."
                    });
                }
            }

            WriteMessageToStdout(response);
        }

        try { _cts.Cancel(); } catch { /* loop ended */ }
    }

    private static async Task<string> HandlePingCommandAsync(CancellationToken ct)
    {
        var ping = await BridgePingEvaluator.EvaluateAsync(ct).ConfigureAwait(false);
        return BridgeJson.Serialize(ping);
    }

    private void WriteMessageToStdout(string jsonMessage)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonMessage);
        var header = BitConverter.GetBytes(bytes.Length);
        lock (_stdoutLock)
        {
            _stdout.Write(header, 0, 4);
            _stdout.Write(bytes, 0, bytes.Length);
            _stdout.Flush();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _cts.CancelAsync().ConfigureAwait(false); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
