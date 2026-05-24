using Fortiva.Core.BrowserBridge;

var key = args.Length > 0 ? args[0] : throw new ArgumentException("key required");
Console.WriteLine(ExtensionIdHelper.ComputeFromManifestKey(key));
