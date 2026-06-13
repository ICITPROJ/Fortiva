namespace Fortiva.Core.Tests.BrowserBridge;

[CollectionDefinition("BrowserBridgeSerial", DisableParallelization = true)]
public sealed class BrowserBridgeSerialCollection : ICollectionFixture<BrowserBridgeTestHost>;
