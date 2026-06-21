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
    /// <summary>
    /// Finds overlapping login groups across the entire vault — imports, manual entries, and edits.
    /// </summary>
    public static IReadOnlyList<VaultDuplicateGroup> FindGroups(IEnumerable<VaultEntry> entries)
    {
        var loginEntries = entries.Where(e => !e.IsSecureNote).ToList();
        if (loginEntries.Count < 2)
            return [];

        var keyToIds = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in loginEntries)
        {
            foreach (var key in GetDuplicateKeys(entry))
            {
                if (!keyToIds.TryGetValue(key, out var ids))
                {
                    ids = [];
                    keyToIds[key] = ids;
                }

                ids.Add(entry.Id);
            }
        }

        var uf = new UnionFind(loginEntries.Select(e => e.Id));
        foreach (var ids in keyToIds.Values)
        {
            if (ids.Count < 2)
                continue;

            var anchor = ids[0];
            for (var i = 1; i < ids.Count; i++)
                uf.Union(anchor, ids[i]);
        }

        var byId = loginEntries.ToDictionary(e => e.Id);
        return uf.Roots()
            .Select(root => uf.Members(root).Select(id => byId[id]).ToList())
            .Where(list => list.Count >= 2)
            .Select(CreateGroup)
            .OrderByDescending(g => g.EntryIds.Count)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IEnumerable<string> GetDuplicateKeys(VaultEntry entry)
    {
        var user = entry.Username.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(user))
            yield break;

        var hosts = CollectHosts(entry);
        foreach (var host in hosts)
        {
            yield return $"h:{host}|{user}";

            var domain = DomainSafety.GetRegistrableDomain(host);
            if (!string.IsNullOrWhiteSpace(domain))
                yield return $"d:{domain}|{user}";
        }

        if (hosts.Count > 0)
            yield break;

        var title = entry.Title.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(title))
            yield return $"t:{title}|{user}";
    }

    internal static List<string> CollectHosts(VaultEntry entry)
    {
        var hosts = new List<string>();

        void AddHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            host = host.Trim();
            if (hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                return;

            hosts.Add(host);
        }

        var url = VaultEntryWebsite.GetEffectiveUrl(entry) ?? entry.Url;
        AddHost(ImportMergeService.ExtractHost(url ?? ""));

        var title = entry.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return hosts;

        AddHost(ImportMergeService.ExtractHost(title));

        var titleUrl = VaultEntryWebsite.GetEffectiveUrl(new VaultEntry { Title = title });
        if (!string.IsNullOrWhiteSpace(titleUrl))
            AddHost(ImportMergeService.ExtractHost(titleUrl));

        return hosts;
    }

    private static VaultDuplicateGroup CreateGroup(List<VaultEntry> list)
    {
        var representative = list
            .OrderByDescending(e => !string.IsNullOrWhiteSpace(VaultEntryWebsite.GetEffectiveUrl(e) ?? e.Url))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .First();

        var samePassword = list.All(e => PasswordsEqual(e.Password, representative.Password));
        var distinctSites = CountDistinctSites(list);

        var kind = distinctSites switch
        {
            > 1 => VaultDuplicateKind.SimilarSite,
            1 when samePassword => VaultDuplicateKind.Exact,
            _ => VaultDuplicateKind.SameSiteUser
        };

        var matchKey = GetDuplicateKeys(representative).FirstOrDefault() ?? representative.Id.ToString();

        return new VaultDuplicateGroup
        {
            MatchKey = matchKey,
            Title = representative.Title,
            Username = representative.Username,
            Url = VaultEntryWebsite.GetEffectiveUrl(representative) ?? representative.Url ?? "",
            Kind = kind,
            EntryIds = list.Select(e => e.Id).ToList()
        };
    }

    private static int CountDistinctSites(List<VaultEntry> list)
    {
        var hosts = list
            .Select(e => ImportMergeService.ExtractHost(VaultEntryWebsite.GetEffectiveUrl(e) ?? e.Url ?? ""))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hosts.Count > 0)
            return hosts.Count;

        return list
            .Select(e => e.Title.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static bool PasswordsEqual(string a, string b)
        => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    private sealed class UnionFind
    {
        private readonly Dictionary<Guid, Guid> _parent = [];

        public UnionFind(IEnumerable<Guid> ids)
        {
            foreach (var id in ids)
                _parent[id] = id;
        }

        public Guid Find(Guid id)
        {
            if (_parent[id] != id)
                _parent[id] = Find(_parent[id]);
            return _parent[id];
        }

        public void Union(Guid a, Guid b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB)
                _parent[rootB] = rootA;
        }

        public IEnumerable<Guid> Roots()
            => _parent.Keys.Select(Find).Distinct();

        public IEnumerable<Guid> Members(Guid root)
            => _parent.Keys.Where(id => Find(id) == root);
    }
}
