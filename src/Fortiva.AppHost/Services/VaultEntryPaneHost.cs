using Fortiva.Core.Vault;

namespace Fortiva.AppHost.Services;

/// <summary>Host callbacks when <see cref="Pages.EntryPage"/> is embedded in the vault detail pane.</summary>
public sealed class VaultEntryPaneHost
{
    public Action? CloseRequested { get; init; }
    public Action? Saved { get; init; }
    public Func<Task<bool>>? ConfirmCloseAsync { get; set; }

    public async Task<bool> TryCloseAsync()
    {
        if (ConfirmCloseAsync is not null && !await ConfirmCloseAsync())
            return false;
        Close();
        return true;
    }

    public void Close() => CloseRequested?.Invoke();
    public void NotifySaved() => Saved?.Invoke();
}

public sealed class EntryPaneNavigationContext
{
    public EntryPaneNavigationContext(VaultEntry entry, VaultEntryPaneHost host)
    {
        Entry = entry;
        Host = host;
    }

    public VaultEntry Entry { get; }
    public VaultEntryPaneHost Host { get; }
}
