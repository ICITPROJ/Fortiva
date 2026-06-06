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
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
    private const int MaxTracked = 64;

    /// <summary>Issues a nonce bound to <paramref name="host"/> (normalized, lower-cased).</summary>
    public string Issue(string host)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var boundHost = NormalizeHost(host);
        lock (_gate)
        {
            PruneIfNeeded();
            _pending[nonce] = boundHost;
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
            if (!_pending.TryGetValue(nonce, out var issuedHost))
                return false;

            if (!string.Equals(issuedHost, boundHost, StringComparison.Ordinal))
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
        => string.IsNullOrWhiteSpace(host) ? "" : host.Trim().ToLowerInvariant();

    private void PruneIfNeeded()
    {
        while (_pending.Count + _consumed.Count > MaxTracked && _consumed.Count > 0)
            _consumed.Clear();
    }
}
