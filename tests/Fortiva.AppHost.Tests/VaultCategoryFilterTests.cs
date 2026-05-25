using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Vault;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class VaultCategoryFilterTests
{
    [Fact]
    public void BuildCategories_IncludesTagsWithCounts()
    {
        var entries = new[]
        {
            CreateEntry("Work", tags: ["Work", "Email"]),
            CreateEntry("Personal", tags: ["Personal"]),
            CreateEntry("No tags")
        }.Select(e => new VaultEntryViewModel(e)).ToList();

        var categories = VaultCategoryFilter.BuildCategories(entries);

        Assert.Contains(categories, c => c.Key == VaultCategoryFilter.AllKey && c.Count == 3);
        Assert.Contains(categories, c => c.Label == "Work" && c.Count == 1);
        Assert.Contains(categories, c => c.Label == "Email" && c.Count == 1);
        Assert.Contains(categories, c => c.Key == VaultCategoryFilter.UntaggedKey && c.Count == 1);
    }

    [Fact]
    public void BuildCategories_CountsDistinctEntriesPerTag()
    {
        var entries = new[]
        {
            CreateEntry("Dup tags", tags: ["Work", "Work", "Email"]),
            CreateEntry("Single", tags: ["Work"])
        }.Select(e => new VaultEntryViewModel(e)).ToList();

        var categories = VaultCategoryFilter.BuildCategories(entries);
        var work = categories.First(c => c.Label == "Work");
        Assert.Equal(2, work.Count);
    }

    [Fact]
    public void Apply_FiltersBySelectedTag()
    {
        var entries = new[]
        {
            CreateEntry("A", tags: ["Finance"]),
            CreateEntry("B", tags: ["Work"]),
            CreateEntry("C")
        }.Select(e => new VaultEntryViewModel(e)).ToList();

        var filtered = VaultCategoryFilter.Apply(entries, "Finance").ToList();

        Assert.Single(filtered);
        Assert.Equal("A", filtered[0].Title);
    }

    [Fact]
    public void BuildCategories_IncludesSavedEmptyCategories()
    {
        var entries = new[]
        {
            CreateEntry("Only work", tags: ["Work"])
        }.Select(e => new VaultEntryViewModel(e)).ToList();

        var categories = VaultCategoryFilter.BuildCategories(entries, ["Finance", "Work"]);

        Assert.Contains(categories, c => c.Label == "Finance" && c.Count == 0);
        Assert.Contains(categories, c => c.Label == "Work" && c.Count == 1);
    }

    [Fact]
    public void IsUserTag_DistinguishesSystemCategories()
    {
        Assert.False(VaultCategoryFilter.IsUserTag(VaultCategoryFilter.AllKey));
        Assert.False(VaultCategoryFilter.IsUserTag(VaultCategoryFilter.FavoritesKey));
        Assert.False(VaultCategoryFilter.IsUserTag(VaultCategoryFilter.UntaggedKey));
        Assert.True(VaultCategoryFilter.IsUserTag("Work"));
    }

    private static VaultEntry CreateEntry(string title, bool favorite = false, params string[] tags)
        => new()
        {
            Title = title,
            IsFavorite = favorite,
            Tags = tags.ToList()
        };
}
