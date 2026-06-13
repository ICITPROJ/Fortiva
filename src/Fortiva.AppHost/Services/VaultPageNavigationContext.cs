namespace Fortiva.AppHost.Services;

public sealed class VaultPageNavigationContext
{
    public string? SearchQuery { get; init; }
    public Guid? ImportBatchId { get; init; }
    public Guid? OpenEntryId { get; init; }
    public bool QuickAdd { get; init; }

    public static VaultPageNavigationContext ForSearch(string query)
        => new() { SearchQuery = query };

    public static VaultPageNavigationContext ForImportBatch(Guid batchId)
        => new() { ImportBatchId = batchId };

    public static VaultPageNavigationContext ForEntry(Guid entryId)
        => new() { OpenEntryId = entryId };

    public static VaultPageNavigationContext ForQuickAdd()
        => new() { QuickAdd = true };
}
