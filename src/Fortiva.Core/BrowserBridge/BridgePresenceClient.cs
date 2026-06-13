using System.IO.Pipes;
using System.Text;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Native host / tooling: query vault presence on the unlock pipe.</summary>
public static class BridgePresenceClient
{
    public static async Task<string?> RequestStatusAsync(
        int timeoutMs = 3000,
        int maxAttempts = 4,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await TryRequestStatusOnceAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
            if (status is not null)
                return status;

            if (attempt < maxAttempts - 1)
                await Task.Delay(100 * (attempt + 1), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<string?> TryRequestStatusOnceAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeoutMs);
            using var client = new NamedPipeClientStream(
                ".", BridgeUnlockBroker.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutMs / 2, linked.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("STATUS".AsMemory(), linked.Token).ConfigureAwait(false);

            return (await reader.ReadLineAsync(linked.Token).ConfigureAwait(false))?.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
