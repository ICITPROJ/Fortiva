using Fortiva.Core.Vault;

namespace Fortiva.Core.ImportExport;

public enum VaultDuplicateKind
{
    /// <summary>Same site + username + password.</summary>
    Exact,
    /// <summary>Same site + username but different passwords (e.g. Keep both on import).</summary>
    SameSiteUser
}

public sealed class VaultDuplicateGroup
{
    public string MatchKey { get; init; } = "";
    public string Title { get; init; } = "";
    public string Username { get; init; } = "";
    public string Url { get; init; } = "";
    public VaultDuplicateKind Kind { get; init; }
    public List<Guid> EntryIds { get; init; } = [];
}

public static class VaultDuplicateAnalyzer
{
    public static IReadOnlyList<VaultDuplicateGroup> FindGroups(IEnumerable<VaultEntry> entries)
    {
        var loginEntries = entries.Where(e => !e.IsSecureNote).ToList();
        var groups = new List<VaultDuplicateGroup>();

        foreach (var bucket in loginEntries.GroupBy(ImportMergeService.BuildMatchKey))
        {
            if (string.IsNullOrEmpty(bucket.Key))
                continue;

            var list = bucket.ToList();
            if (list.Count < 2)
                continue;

            var representative = list[0];
            var samePassword = list.All(e => PasswordsEqual(e.Password, representative.Password));

            groups.Add(new VaultDuplicateGroup
            {
                MatchKey = bucket.Key,
                Title = representative.Title,
                Username = representative.Username,
                Url = representative.Url ?? "",
                Kind = samePassword ? VaultDuplicateKind.Exact : VaultDuplicateKind.SameSiteUser,
                EntryIds = list.Select(e => e.Id).ToList()
            });
        }

        return groups
            .OrderByDescending(g => g.EntryIds.Count)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PasswordsEqual(string a, string b)
        => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);
}
