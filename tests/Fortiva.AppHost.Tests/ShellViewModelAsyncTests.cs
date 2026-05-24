using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Vault;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class ShellViewModelAsyncTests : IDisposable
{
    private readonly string _vaultDir;
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private readonly List<Action> _uiQueue = [];

    public ShellViewModelAsyncTests()
    {
        _vaultDir = Path.Combine(Path.GetTempPath(), "FortivaVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultDir);
        _vm.ResetForTesting();
        _vm.SetUiInvoker(action => _uiQueue.Add(action));
        _vm.PreparePortableVaultLocation(_vaultDir);
    }

    public void Dispose()
    {
        _vm.ResetForTesting();
        _vm.SetUiInvoker(action => action());
        if (Directory.Exists(_vaultDir))
            Directory.Delete(_vaultDir, recursive: true);
    }

    private void DrainUiQueue()
    {
        while (_uiQueue.Count > 0)
        {
            var batch = _uiQueue.ToArray();
            _uiQueue.Clear();
            foreach (var action in batch)
                action();
        }
    }

    [Fact]
    public async Task CreateVaultAsync_UpdatesStateOnUiInvoker()
    {
        await _vm.CreateVaultAsync("StrongTestPassword123!", SecurityLevel.Standard);
        DrainUiQueue();

        Assert.True(_vm.VaultExists);
        Assert.True(File.Exists(Path.Combine(_vaultDir, VaultConstants.VaultFileName)));
    }

    [Fact]
    public async Task UnlockAsync_MarshalsResultsThroughUiInvoker()
    {
        await _vm.CreateVaultAsync("StrongTestPassword123!", SecurityLevel.Standard);
        DrainUiQueue();

        _vm.ResetForTesting();
        _vm.SetUiInvoker(action => _uiQueue.Add(action));
        _vm.SwitchToPortableVault(_vaultDir);

        var (ok, error) = await _vm.UnlockAsync("StrongTestPassword123!");
        DrainUiQueue();

        Assert.True(ok, error);
        Assert.True(_vm.IsUnlocked);
    }
}
