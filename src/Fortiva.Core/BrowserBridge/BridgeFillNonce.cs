using System.Security.Cryptography;

namespace Fortiva.Core.BrowserBridge;

/// <summary>Single-use nonces for browser bridge password fills (replay resistance).</summary>
public sealed class BridgeFillNonce
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
    private const int MaxTracked = 64;

    public string Issue()
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (_gate)
        {
            PruneIfNeeded();
            _pending.Add(nonce);
        }

        return nonce;
    }

    public bool TryConsume(string? nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return false;

        lock (_gate)
        {
            if (!_pending.Remove(nonce))
                return false;

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

    private void PruneIfNeeded()
    {
        while (_pending.Count + _consumed.Count > MaxTracked && _consumed.Count > 0)
            _consumed.Clear();
    }
}
