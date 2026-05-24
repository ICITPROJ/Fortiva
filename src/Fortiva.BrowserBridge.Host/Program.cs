using System.Text;
using System.Text.Json;
using Fortiva.Core.BrowserBridge;

/// <summary>
/// Chrome/Edge native messaging host: forwards requests to Fortiva named pipe server.
/// </summary>
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
    var doc = JsonDocument.Parse(json);
    var response = await ForwardToPipeAsync(doc.RootElement);
    var respBytes = Encoding.UTF8.GetBytes(response);
    var header = BitConverter.GetBytes(respBytes.Length);
    await stdout.WriteAsync(header);
    await stdout.WriteAsync(respBytes);
}

static async Task<string> ForwardToPipeAsync(JsonElement request)
{
    try
    {
        var token = await BridgeSessionAuth.RequestTokenFromBrokerAsync();
        if (string.IsNullOrEmpty(token))
            return JsonSerializer.Serialize(new CredentialResponse());

        using var client = new System.IO.Pipes.NamedPipeClientStream(
            ".", BrowserBridgeServer.PipeName, System.IO.Pipes.PipeDirection.InOut);
        await client.ConnectAsync(2000);
        using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8);

        var envelope = new Dictionary<string, object?>
        {
            ["command"] = request.TryGetProperty("command", out var cmd) ? cmd.GetString() : "",
            ["SessionToken"] = token
        };
        if (request.TryGetProperty("payload", out var payload))
            envelope["payload"] = payload;

        await writer.WriteLineAsync(JsonSerializer.Serialize(envelope));
        var line = await reader.ReadLineAsync();
        return line ?? "{}";
    }
    catch
    {
        return JsonSerializer.Serialize(new CredentialResponse());
    }
}
