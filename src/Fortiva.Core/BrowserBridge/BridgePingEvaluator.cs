namespace Fortiva.Core.BrowserBridge;

/// <summary>End-to-end native-host ping: token probe, presence with retries, classification.</summary>
public static class BridgePingEvaluator
{
    private static readonly TimeSpan OverallBudget = TimeSpan.FromSeconds(5);

    public static async Task<BridgeStatusResponse> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(OverallBudget);
        try
        {
            return await EvaluateCoreAsync(overall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "bridge_warming",
                Message = "Fortiva is starting the bridge. Wait a moment, then click Fill again."
            };
        }
        catch
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "setup_required",
                Message = "Run Connect browser in Fortiva Settings."
            };
        }
    }

    private static async Task<BridgeStatusResponse> EvaluateCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!BridgeProcessCheck.IsFortivaRunning())
                return BridgePingClassifier.Classify(null, null, fortivaRunning: false);

            var presence = await BridgePresenceClient.RequestStatusAsync(
                timeoutMs: 600,
                maxAttempts: 2,
                cancellationToken).ConfigureAwait(false);

            if (BridgePresenceStatus.IsExplicitlyLocked(presence))
                return BridgePingClassifier.Classify(null, presence);

            if (string.Equals(presence, BridgePresenceStatus.NoVault, StringComparison.OrdinalIgnoreCase))
                return BridgePingClassifier.Classify(null, presence);

            var token = await BridgeSessionAuth.RequestTokenFromBrokerAsync(800, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                return BridgePingClassifier.Classify(token, presence);

            if (BridgePresenceStatus.IsBridgeReady(presence))
                return BridgePingClassifier.Classify(null, presence);

            if (BridgePresenceStatus.IsUnlocked(presence))
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                token = await BridgeSessionAuth.RequestTokenFromBrokerAsync(800, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                    return BridgePingClassifier.Classify(token, presence);
            }

            return BridgePingClassifier.Classify(null, presence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new BridgeStatusResponse
            {
                Ok = false,
                Status = "setup_required",
                Message = "Run Connect browser in Fortiva Settings."
            };
        }
    }
}
