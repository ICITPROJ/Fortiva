using System.Security.Cryptography;

namespace Fortiva.Core.BrowserBridge;

/// <summary>
/// Single-use nonces for browser bridge password fills (replay resistance).
/// Each nonce is bound to the host it was issued for, so a nonce obtained from a
/// <c>list_credentials</c> call for one domain cannot be replayed to fetch credentials
/// for a different domain.
/// </summary>
public sealed class BridgeFillNonce
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Host, DateTimeOffset IssuedAt)> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
    private const int MaxTracked = 64;
    private static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(2);

    /// <summary>Issues a nonce bound to <paramref name="host"/> (normalized, lower-cased).</summary>
    public string Issue(string host)
    {
        var boundHost = NormalizeHost(host);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (_gate)
        {
            PruneExpired();
            InvalidatePendingForHost(boundHost);
            _pending[nonce] = (boundHost, DateTimeOffset.UtcNow);
            PruneIfNeeded();
        }

        return nonce;
    }

    /// <summary>
    /// Consumes a nonce only if it was issued for <paramref name="host"/>. Single-use.
    /// </summary>
    public bool TryConsume(string? nonce, string host)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return false;

        var boundHost = NormalizeHost(host);
        lock (_gate)
        {
            PruneExpired();
            if (!_pending.TryGetValue(nonce, out var entry))
                return false;

            if (!string.Equals(entry.Host, boundHost, StringComparison.Ordinal))
                return false;

            _pending.Remove(nonce);
            _consumed.Add(nonce);
            PruneIfNeeded();
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _consumed.Clear();
        }
    }

    private static string NormalizeHost(string? host)
        => string.IsNullOrWhiteSpace(host) ? "" : DomainSafety.NormalizeHost(host);

    private void InvalidatePendingForHost(string host)
    {
        foreach (var key in _pending.Where(kv => kv.Value.Host == host).Select(kv => kv.Key).ToList())
            _pending.Remove(key);
    }

    private void PruneExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - NonceTtl;
        foreach (var key in _pending.Where(kv => kv.Value.IssuedAt < cutoff).Select(kv => kv.Key).ToList())
            _pending.Remove(key);
    }

    private void PruneIfNeeded()
    {
        while (_pending.Count + _consumed.Count > MaxTracked && _consumed.Count > 0)
            _consumed.Clear();
    }
}
