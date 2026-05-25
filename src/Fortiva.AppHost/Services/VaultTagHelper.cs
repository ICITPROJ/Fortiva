namespace Fortiva.AppHost.Services;

/// <summary>Normalizes and merges vault tags / sidebar categories.</summary>
public static class VaultTagHelper
{
    public const int MaxTagLength = 48;
    public const int MaxTagsPerEntry = 16;

    public static string? NormalizeTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxTagLength)
            trimmed = trimmed[..MaxTagLength];

        return trimmed;
    }

    public static IReadOnlyList<string> ParseTags(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return [];

        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTag)
            .Where(t => t is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTagsPerEntry)
            .ToList();
    }

    public static string JoinTags(IEnumerable<string> tags)
        => string.Join(", ", tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> CollectKnownTags(
        IEnumerable<string> entryTags,
        IEnumerable<string>? savedCategories = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in entryTags.Concat(savedCategories ?? []))
        {
            var normalized = NormalizeTag(tag);
            if (normalized is not null)
                set.Add(normalized);
        }

        return set.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
