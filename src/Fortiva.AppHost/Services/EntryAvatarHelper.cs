using System.Security.Cryptography;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Fortiva.AppHost.Services;

/// <summary>Domain-tinted entry avatars (Keeper / 1Password-style colored initials).</summary>
public static class EntryAvatarHelper
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 64, 188, 244),   // brand cyan
        Color.FromArgb(255, 88, 166, 255),   // blue
        Color.FromArgb(255, 124, 92, 255),   // violet
        Color.FromArgb(255, 52, 199, 89),    // green
        Color.FromArgb(255, 255, 149, 0),    // amber
        Color.FromArgb(255, 255, 105, 97),   // coral
        Color.FromArgb(255, 90, 200, 250),   // sky
        Color.FromArgb(255, 175, 82, 222),   // purple
    ];

    public static Brush GetBackgroundBrush(string? url, string? title)
    {
        var key = ResolveAvatarKey(url, title);
        var color = Palette[StableIndex(key) % Palette.Length];
        return new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));
    }

    public static Brush GetForegroundBrush(string? url, string? title)
    {
        var key = ResolveAvatarKey(url, title);
        var color = Palette[StableIndex(key) % Palette.Length];
        return new SolidColorBrush(color);
    }

    public static Brush GetBorderBrush(string? url, string? title)
    {
        var key = ResolveAvatarKey(url, title);
        var color = Palette[StableIndex(key) % Palette.Length];
        return new SolidColorBrush(Color.FromArgb(96, color.R, color.G, color.B));
    }

    private static string ResolveAvatarKey(string? url, string? title)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var host = new Uri(url.Trim()).Host;
                if (!string.IsNullOrWhiteSpace(host))
                    return host.ToLowerInvariant();
            }
            catch
            {
                /* fall through */
            }
        }

        return string.IsNullOrWhiteSpace(title) ? "fortiva" : title.Trim().ToLowerInvariant();
    }

    private static int StableIndex(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }
}
