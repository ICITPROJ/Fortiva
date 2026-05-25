using Fortiva.AppHost.ViewModels;

namespace Fortiva.AppHost.Services;

public sealed class VaultCategoryItem
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string IconGlyph { get; init; }
    public int Count { get; init; }
}

public static class VaultCategoryFilter
{
    public const string AllKey = "";
    public const string FavoritesKey = "__favorites__";
    public const string UntaggedKey = "__untagged__";

    public static bool IsUserTag(string? key)
        => !string.IsNullOrEmpty(key)
           && key != AllKey
           && key != FavoritesKey
           && key != UntaggedKey;

    public static IReadOnlyList<VaultCategoryItem> BuildCategories(
        IEnumerable<VaultEntryViewModel> entries,
        IEnumerable<string>? savedCategories = null)
    {
        var all = entries.ToList();
        var items = new List<VaultCategoryItem>
        {
            new()
            {
                Key = AllKey,
                Label = "All entries",
                IconGlyph = "\uE8FD",
                Count = all.Count
            },
            new()
            {
                Key = FavoritesKey,
                Label = "Favorites",
                IconGlyph = "\uE734",
                Count = all.Count(e => e.IsFavorite)
            },
            new()
            {
                Key = UntaggedKey,
                Label = "Untagged",
                IconGlyph = "\uE7BA",
                Count = all.Count(e => e.Entry.Tags.Count == 0)
            }
        };

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var saved in savedCategories ?? [])
        {
            var normalized = VaultTagHelper.NormalizeTag(saved);
            if (normalized is not null)
                tagCounts.TryAdd(normalized, 0);
        }

        foreach (var entry in all)
        {
            var tagsInEntry = entry.Entry.Tags
                .Select(VaultTagHelper.NormalizeTag)
                .Where(t => t is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var normalized in tagsInEntry)
            {
                tagCounts.TryGetValue(normalized, out var count);
                tagCounts[normalized] = count + 1;
            }
        }

        foreach (var tag in tagCounts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new VaultCategoryItem
            {
                Key = tag.Key,
                Label = tag.Key,
                IconGlyph = "\uE8E7",
                Count = tag.Value
            });
        }

        return items;
    }

    public static IEnumerable<VaultEntryViewModel> Apply(
        IEnumerable<VaultEntryViewModel> entries,
        string? categoryKey)
    {
        if (string.IsNullOrEmpty(categoryKey) || categoryKey == AllKey)
            return entries;

        return categoryKey switch
        {
            FavoritesKey => entries.Where(e => e.IsFavorite),
            UntaggedKey => entries.Where(e => e.Entry.Tags.Count == 0),
            _ => entries.Where(e =>
                e.Entry.Tags.Any(t => string.Equals(t.Trim(), categoryKey, StringComparison.OrdinalIgnoreCase)))
        };
    }
}
