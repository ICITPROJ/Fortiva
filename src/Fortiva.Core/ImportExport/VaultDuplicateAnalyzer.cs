using Fortiva.Core.BrowserBridge;
using Fortiva.Core.Vault;

namespace Fortiva.Core.ImportExport;

public enum VaultDuplicateKind
{
    /// <summary>Same site + username + password.</summary>
    Exact,
    /// <summary>Same site + username but different passwords (e.g. Keep both on import).</summary>
    SameSiteUser,
    /// <summary>Same registrable domain + username but different hosts or URL paths.</summary>
    SimilarSite
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
        var handled = new HashSet<Guid>();

        foreach (var bucket in loginEntries.GroupBy(BuildSiteKey, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(bucket.Key))
                continue;

            var list = bucket.ToList();
            if (list.Count < 2)
                continue;

            groups.Add(CreateGroup(bucket.Key, list));
            foreach (var entry in list)
                handled.Add(entry.Id);
        }

        foreach (var bucket in loginEntries.Where(e => !handled.Contains(e.Id)).GroupBy(ImportMergeService.BuildMatchKey))
        {
            if (string.IsNullOrEmpty(bucket.Key))
                continue;

            var list = bucket.ToList();
            if (list.Count < 2)
                continue;

            groups.Add(CreateGroup(bucket.Key, list));
        }

        return groups
            .OrderByDescending(g => g.EntryIds.Count)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string BuildSiteKey(VaultEntry entry)
    {
        var url = VaultEntryWebsite.GetEffectiveUrl(entry) ?? entry.Url;
        var host = ImportMergeService.ExtractHost(url ?? "");
        if (string.IsNullOrWhiteSpace(host))
            return "";

        var domain = DomainSafety.GetRegistrableDomain(host);
        var user = entry.Username.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(user))
            return "";

        return $"{domain}|{user}";
    }

    private static VaultDuplicateGroup CreateGroup(string key, List<VaultEntry> list)
    {
        var representative = list[0];
        var samePassword = list.All(e => PasswordsEqual(e.Password, representative.Password));
        var distinctHosts = list
            .Select(e => ImportMergeService.ExtractHost(VaultEntryWebsite.GetEffectiveUrl(e) ?? e.Url ?? ""))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var kind = distinctHosts switch
        {
            > 1 => VaultDuplicateKind.SimilarSite,
            1 when samePassword => VaultDuplicateKind.Exact,
            _ => VaultDuplicateKind.SameSiteUser
        };

        return new VaultDuplicateGroup
        {
            MatchKey = key,
            Title = representative.Title,
            Username = representative.Username,
            Url = VaultEntryWebsite.GetEffectiveUrl(representative) ?? representative.Url ?? "",
            Kind = kind,
            EntryIds = list.Select(e => e.Id).ToList()
        };
    }

    private static bool PasswordsEqual(string a, string b)
        => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);
}
