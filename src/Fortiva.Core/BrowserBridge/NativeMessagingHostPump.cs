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

        string? command = null;
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
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(msgBuf), new JsonDocumentOptions { MaxDepth = 16 });
                command = doc.RootElement.TryGetProperty("command", out var cmd) ? cmd.GetString() : null;

                if (!BridgePipeNaming.HasActiveSession(_enterprise))
                {
                    response = SerializeNoSessionResponse(command);
                }
                else
                {
                    var timeoutSeconds = string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase)
                        ? ExecuteFillTimeoutSeconds
                        : RequestTimeoutSeconds;

                    var handleTask = BridgeNativeForwarder.HandleAsync(doc.RootElement, CancellationToken.None);
                    var completed = await Task.WhenAny(handleTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)))
                        .ConfigureAwait(false);

                    response = completed == handleTask
                        ? await handleTask.ConfigureAwait(false)
                        : SerializeTimeoutResponse(command);
                }
            }
            catch
            {
                response = SerializeTimeoutResponse(command);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(response);
        NativeMessagingFraming.WriteLengthPrefixedMessage(_stdout, bytes);
    }

    private static string SerializeNoSessionResponse(string? command)
    {
        if (string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "setup_required",
                Message = "Fortiva is not running. Open Fortiva and unlock your vault."
            });
        }

        return BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
        {
            Status = new BridgeStatusBlock
            {
                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                VaultUnlocked = false,
                Error = "host_unreachable"
            }
        });
    }

    private static string SerializeTimeoutResponse(string? command)
    {
        if (string.Equals(command, "execute_fill", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeJson.Serialize(new CredentialResponse
            {
                Error = "internal_error",
                Message = "Fortiva bridge timed out. Unlock Fortiva and try Fill again."
            });
        }

        return BridgeJson.Serialize(new BridgeStatusAndMatchesResponse
        {
            Status = new BridgeStatusBlock
            {
                AppRunning = BridgeProcessCheck.IsFortivaRunning(),
                VaultUnlocked = false,
                Error = "internal_error"
            }
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
