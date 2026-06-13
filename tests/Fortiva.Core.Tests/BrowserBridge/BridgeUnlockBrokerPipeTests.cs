using System.IO.Pipes;

using System.Text;

using Fortiva.Core.BrowserBridge;



namespace Fortiva.Core.Tests.BrowserBridge;



[Collection("BrowserBridgeSerial")]

public class BridgeUnlockBrokerPipeTests

{

    [Fact]
    public async Task StatusRoundTrip_UnauthenticatedClient_DoesNotLeakPresence()
    {
        BridgePipeNaming.RotateSessionId(false);
        var pipeName = BridgePipeNaming.UnlockPipeName(false);

        using var broker = new BridgeUnlockBroker(
            () => false,
            () => true,
            _ => Task.FromResult(true));

        broker.Start();

        using var client = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await client.ConnectAsync(connectCts.Token);



        try

        {

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync("STATUS");



            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            var line = await reader.ReadLineAsync(readCts.Token);



            if (line is not null)

                Assert.Equal(BridgePresenceStatus.Unknown, line.Trim());

        }

        catch (IOException)

        {

            // Listener may disconnect unauthenticated clients before any response is sent.

        }

    }

}


