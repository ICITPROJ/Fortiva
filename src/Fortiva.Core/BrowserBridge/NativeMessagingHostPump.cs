using System.Text;
using System.Text.Json;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// One-shot native messaging host: read one stdin request, write one stdout response, exit.
/// </summary>
public sealed class NativeMessagingHostPump : IAsyncDisposable
{
    private const int RequestTimeoutSeconds = 5;
    private const int ExecuteFillTimeoutSeconds = 30;

    private readonly bool _enterprise;
    private readonly bool _integrityOk;
    private readonly Stream _stdin;
    private readonly Stream _stdout;

    public NativeMessagingHostPump(bool enterprise, bool integrityOk, Stream? stdin = null, Stream? stdout = null)
    {
        _enterprise = enterprise;
        _integrityOk = integrityOk;
        _stdin = stdin ?? Console.OpenStandardInput();
        _stdout = stdout ?? Console.OpenStandardOutput();
    }

    public async Task RunAsync()
    {
        var lengthBuf = new byte[4];
        var read = await _stdin.ReadAsync(lengthBuf.AsMemory(0, 4)).ConfigureAwait(false);
        if (read < 4)
            return;

        var len = BitConverter.ToInt32(lengthBuf, 0);
        if (len <= 0 || len > 1024 * 1024)
            return;

        var msgBuf = new byte[len];
        var offset = 0;
        while (offset < len)
        {
            var chunk = await _stdin.ReadAsync(msgBuf.AsMemory(offset, len - offset)).ConfigureAwait(false);
            if (chunk == 0)
                return;
            offset += chunk;
        }

        string response;
        if (!_integrityOk)
        {
            response = BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
            {
                Status = new BridgeStatusBlock
                {
                    AppRunning = false,
                    VaultUnlocked = false,
                    Error = "host_unreachable"
                }
            });
        }
        else
        {
            try
            {
                if (!BridgePipeNaming.HasActiveSession(_enterprise))
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(msgBuf), new JsonDocumentOptions { MaxDepth = 16 });
                    var command = doc.RootElement.TryGetProperty("command", out var cmd) ? cmd.GetString() : null;
                    response = string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase)
                        ? BridgeJson.Serialize(new CredentialResponse
                        {
                            Error = "setup_required",
                            Message = "Fortiva is not running. Open Fortiva and unlock your vault."
                        })
                        : BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
                        {
                            Status = new BridgeStatusBlock
                            {
                                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                                VaultUnlocked = false,
                                Error = "host_unreachable"
                            }
                        });
                }
                else
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(msgBuf), new JsonDocumentOptions { MaxDepth = 16 });
                    var command = doc.RootElement.TryGetProperty("command", out var cmd)
                        ? cmd.GetString()
                        : null;

                    using var reqCts = new CancellationTokenSource();
                    var timeoutSeconds = string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase)
                        ? ExecuteFillTimeoutSeconds
                        : RequestTimeoutSeconds;
                    reqCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    var handleTask = BridgeNativeForwarder.HandleAsync(doc.RootElement, reqCts.Token);
                    var completed = await Task.WhenAny(handleTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), reqCts.Token))
                        .ConfigureAwait(false);
                    response = completed == handleTask
                        ? await handleTask.ConfigureAwait(false)
                        : BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
                        {
                            Status = new BridgeStatusBlock
                            {
                                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                                VaultUnlocked = false,
                                Error = "internal_error"
                            }
                        });
                }
            }
            catch (OperationCanceledException)
            {
                response = BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
                {
                    Status = new BridgeStatusBlock
                    {
                        AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                        VaultUnlocked = false,
                        Error = "internal_error"
                    }
                });
            }
            catch
            {
                response = BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
                {
                    Status = new BridgeStatusBlock
                    {
                        AppRunning = false,
                        VaultUnlocked = false,
                        Error = "internal_error"
                    }
                });
            }
        }

        var bytes = Encoding.UTF8.GetBytes(response);
        NativeMessagingFraming.WriteLengthPrefixedMessage(_stdout, bytes);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
