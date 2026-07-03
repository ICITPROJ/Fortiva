using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fortiva.Core.BrowserBridge;

public static class BridgeJson
{
    /// <summary>Maximum accepted length of a single bridge request line (defense against memory DoS).</summary>
    public const int MaxRequestBytes = 1 << 20; // 1 MB, matches native-messaging stdin cap

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string SerializeSessionToken(BridgeSessionTokenResponse value) =>
        JsonSerializer.Serialize(value, SessionTokenOptions);

    private static readonly JsonSerializerOptions SessionTokenOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 16
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>
    /// Reads a single newline-terminated request line, rejecting input larger than
    /// <see cref="MaxRequestBytes"/>. Returns null at end of stream or when the cap is exceeded.
    /// </summary>
    public static async Task<string?> ReadBoundedLineAsync(TextReader reader, CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                return sb.Length == 0 ? null : sb.ToString();

            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c == '\n')
                    return sb.ToString().TrimEnd('\r');
                sb.Append(c);
                if (sb.Length > MaxRequestBytes)
                    return null;
            }
        }
    }
}
