using System.Net;

namespace Fortiva.Core.Updates;

/// <summary>
/// HTTP client that validates every redirect target against the update URL allowlist.
/// </summary>
internal static class SecureUpdateHttp
{
    private const int MaxRedirects = 5;

    private static readonly HttpClient Client = CreateClient();

    public static Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        => GetStringAsync(url, UpdateUrlPolicy.ValidateManifestUrl, cancellationToken);

    public static Task<HttpResponseMessage> GetInstallerResponseAsync(
        string url,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default)
        => SendWithRedirectValidationAsync(
            url,
            UpdateUrlPolicy.ValidateInstallerUrl,
            completionOption,
            cancellationToken);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task<string> GetStringAsync(
        string url,
        Action<string> validateUrl,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRedirectValidationAsync(
            url,
            validateUrl,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendWithRedirectValidationAsync(
        string url,
        Action<string> validateUrl,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        validateUrl(url);
        var current = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await Client.SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                current = ResolveRedirect(current, location);
                response.Dispose();
                validateUrl(current);
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("Update download exceeded the maximum number of redirects.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => (int)statusCode is >= 300 and < 400;

    private static string ResolveRedirect(string currentUrl, Uri location)
    {
        if (location.IsAbsoluteUri)
            return location.ToString();

        var baseUri = new Uri(currentUrl);
        return new Uri(baseUri, location).ToString();
    }
}
