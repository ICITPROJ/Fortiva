using System.Security.Cryptography;
using System.Text.Json;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Derives the stable Chrome/Edge extension ID from manifest.json "key".</summary>
public static class ExtensionIdHelper
{
    public static string ComputeFromManifestKey(string base64PublicKey)
    {
        if (string.IsNullOrWhiteSpace(base64PublicKey))
            throw new ArgumentException("Manifest key is required.", nameof(base64PublicKey));

        var keyBytes = Convert.FromBase64String(base64PublicKey);
        var hash = SHA256.HashData(keyBytes);
        Span<char> id = stackalloc char[32];
        for (var i = 0; i < 16; i++)
        {
            id[i * 2] = (char)('a' + (hash[i] >> 4));
            id[i * 2 + 1] = (char)('a' + (hash[i] & 0xF));
        }
        return new string(id);
    }

    public static string ReadFromManifestFile(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("key", out var keyProp))
            throw new InvalidOperationException("manifest.json must contain a 'key' field.");
        return ComputeFromManifestKey(keyProp.GetString()!);
    }

    public static string? ReadVersionFromManifestFile(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return doc.RootElement.TryGetProperty("version", out var versionProp)
            ? versionProp.GetString()
            : null;
    }
}
