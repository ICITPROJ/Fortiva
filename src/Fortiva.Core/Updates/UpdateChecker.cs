using System.Net.Http;
using System.Security.Cryptography;
using Fortiva.Core.Platform;

namespace Fortiva.Core.Updates;

public sealed class UpdateCheckResult
{
    public required UpdateStatus Status { get; init; }
    public ReleaseManifest? Manifest { get; init; }
    public string? Message { get; init; }
    public PlatformCompatibility Platform { get; init; } = PlatformCompatibility.Supported();
    public bool IsOnlineManifest { get; init; }
}

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    PlatformUnsupported,
    PlatformUntested,
    CheckFailed
}

public sealed class UpdateChecker
{
    private readonly ReleaseManifestLoader _loader = new();

    public async Task<UpdateCheckResult> CheckAsync(
        string manifestUrl,
        string currentVersion,
        string? bundledManifestPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = ReleaseManifestUrls.ResolvePersonalLatest(manifestUrl);
            ReleaseManifest? manifest;
            var fromNetwork = false;

            if (ReleaseManifestUrls.IsLocalManifestPath(resolved))
            {
                manifest = _loader.TryLoadBundled(resolved);
                if (manifest is null || !manifest.IsValid)
                    return Fail(UpdateMessages.ManifestUnavailableWithManualInstall);

                return ReleaseManifestEvaluator.Evaluate(manifest, currentVersion, fromNetwork: false);
            }

            (manifest, fromNetwork) = await _loader.TryLoadAsync(
                manifestUrl,
                bundledManifestPath,
                cancellationToken).ConfigureAwait(false);

            if (manifest is null || !manifest.IsValid)
                return Fail(UpdateMessages.ManifestUnavailableWithManualInstall);

            // Shipped placeholder manifest must not satisfy update checks when GitHub is unreachable.
            if (!fromNetwork)
                return Fail(UpdateMessages.ManifestUnavailableWithManualInstall);

            return ReleaseManifestEvaluator.Evaluate(manifest, currentVersion, fromNetwork);
        }
        catch (Exception ex)
        {
            return Fail(UpdateMessages.ForCheckFailure(ex));
        }
    }

    public async Task<string> DownloadVerifiedAsync(
        ReleaseManifest manifest,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        UpdateUrlPolicy.ValidateInstallerUrl(manifest.InstallerUrl);

        using var response = await SecureUpdateHttp.GetInstallerResponseAsync(
            manifest.InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(destinationPath);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        file.Close();

        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(destinationPath, cancellationToken)))
            .ToLowerInvariant();
        var expected = manifest.InstallerSha256.ToLowerInvariant();
        if (!hash.Equals(expected, StringComparison.Ordinal))
        {
            File.Delete(destinationPath);
            throw new InvalidOperationException("Downloaded installer failed SHA-256 verification.");
        }

        return destinationPath;
    }

    private static UpdateCheckResult Fail(string message) => new()
    {
        Status = UpdateStatus.CheckFailed,
        Message = message
    };
}
