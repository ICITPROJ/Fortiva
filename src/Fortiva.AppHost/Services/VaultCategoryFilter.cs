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

    public static IReadOnlyList<VaultCategoryItem> BuildCategories(IEnumerable<VaultEntryViewModel> entries)
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

        var tags = all
            .SelectMany(e => e.Entry.Tags)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            items.Add(new VaultCategoryItem
            {
                Key = tag.Key,
                Label = tag.Key,
                IconGlyph = "\uE8E7",
                Count = all.Count(e =>
                    e.Entry.Tags.Any(t => string.Equals(t.Trim(), tag.Key, StringComparison.OrdinalIgnoreCase)))
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
