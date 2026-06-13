using System.IO.Pipes;
using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Tests.BrowserBridge;

[Collection("BrowserBridgeSerial")]
public sealed class BrowserBridgeCorePipeStressTests : IDisposable
{
    public BrowserBridgeCorePipeStressTests()
    {
        AuthenticodePolicy.ConfigureForEdition("Personal");
        Environment.SetEnvironmentVariable("FORTIVA_ALLOW_UNSIGNED_BRIDGE", "1");
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_DISABLE_PIPE_VALIDATION", "1");
        Environment.SetEnvironmentVariable("FORTIVA_BRIDGE_FAST_TEST", "1");
    }

    public void Dispose()
    {
        BridgeSessionAuth.ClearSessionToken();
        BridgeSessionAuth.ConfigureTokenDirectory(null!);
        BridgeHostProcessCleanup.StopAllHosts();
        Thread.Sleep(300);
    }

    [Fact]
    public async Task TokenBroker_RepeatedRejectedConnections_StayAvailable()
    {
        BridgeTestEnvironment.EnsurePipeNamesAvailable();

        var token = BridgeSessionAuth.CreateSessionToken();
        var broker = new BridgeTokenBroker(token);
        try
        {
            broker.Start();
            await Task.Delay(100);

            var connected = 0;
            for (var i = 0; i < 30; i++)
            {
                if (await TryConnectAsync(BridgeTokenBroker.PipeName, timeoutMs: 1200))
                    connected++;
            }

            Assert.True(connected >= 24, $"Expected most connects to succeed; got {connected}/30");
        }
        finally
        {
            broker.DisposeBlocking();
        }
    }

    [Fact]
    public async Task TokenBroker_ParallelRejectedConnections_DoNotDeadlock()
    {
        BridgeTestEnvironment.EnsurePipeNamesAvailable();

        var token = BridgeSessionAuth.CreateSessionToken();
        var broker = new BridgeTokenBroker(token);
        try
        {
            broker.Start();
            await Task.Delay(250);

            var tasks = Enumerable.Range(0, 16)
                .Select(_ => TryConnectAsync(BridgeTokenBroker.PipeName, timeoutMs: 5000));
            var results = await Task.WhenAll(tasks);
            Assert.True(results.Any(r => r), "At least one parallel connect should succeed");

            var sequential = 0;
            for (var i = 0; i < 12; i++)
            {
                if (await TryConnectAsync(BridgeTokenBroker.PipeName, timeoutMs: 5000))
                    sequential++;
            }

            Assert.True(sequential >= 10, $"Expected pipe to recover after parallel burst; got {sequential}/12");
        }
        finally
        {
            broker.DisposeBlocking();
        }
    }

    [Fact]
    public async Task BrowserBridgeServer_RepeatedConnections_StayAvailable()
    {
        BridgeTestEnvironment.EnsurePipeNamesAvailable();

        var token = BridgeSessionAuth.CreateSessionToken();
        var server = new BrowserBridgeServer(
            _ => new CredentialResponse { Found = true, Username = "u", Password = "p" },
            _ => new CredentialResponse { Matches = [] },
            token);
        try
        {
            server.Start();
            await Task.Delay(250);

            var connected = 0;
            for (var i = 0; i < 30; i++)
            {
                if (await TryConnectAsync(BrowserBridgeServer.PipeName, timeoutMs: 1200))
                    connected++;
            }

            Assert.True(connected >= 24, $"Expected most connects to succeed; got {connected}/30");
        }
        finally
        {
            server.DisposeBlocking();
        }
    }

    [Fact]
    public async Task RestartInfrastructure_DisposeAndRecreate_AcceptsConnections()
    {
        BridgeTestEnvironment.EnsurePipeNamesAvailable();

        var token = BridgeSessionAuth.CreateSessionToken();
        var tokenBroker = new BridgeTokenBroker(token);
        try
        {
            tokenBroker.Start();
            await Task.Delay(250);
            Assert.True(await TryConnectAsync(BridgeTokenBroker.PipeName, timeoutMs: 5000));
        }
        finally
        {
            tokenBroker.DisposeBlocking();
        }

        await Task.Delay(400);

        var token2 = BridgeSessionAuth.CreateSessionToken();
        var tokenBroker2 = new BridgeTokenBroker(token2);
        try
        {
            tokenBroker2.Start();
            await Task.Delay(250);
            Assert.True(await TryConnectAsync(BridgeTokenBroker.PipeName, timeoutMs: 5000));
        }
        finally
        {
            tokenBroker2.DisposeBlocking();
        }
    }

    private static async Task<bool> TryConnectAsync(string pipeName, int timeoutMs = 1000)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutMs);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
