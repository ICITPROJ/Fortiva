using Fortiva.AppHost.ViewModels;
using Fortiva.Core.Vault;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class ShellViewModelAsyncTests : IDisposable
{
    private readonly string _vaultDir;
    private readonly ShellViewModel _vm = ShellViewModel.Current;
    private int _uiInvokerCalls;

    public ShellViewModelAsyncTests()
    {
        _vaultDir = Path.Combine(Path.GetTempPath(), "FortivaVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultDir);
        _vm.ResetForTesting();
        // RunOnUiAsync awaits invoker completion — queued-only invokers deadlock UnlockAsync.
        _vm.SetUiInvoker(action =>
        {
            _uiInvokerCalls++;
            action();
        });
        _vm.PreparePortableVaultLocation(_vaultDir);
    }

    public void Dispose()
    {
        _vm.ResetForTesting();
        _vm.SetUiInvoker(action => action());
        if (Directory.Exists(_vaultDir))
            Directory.Delete(_vaultDir, recursive: true);
    }

    [Fact]
    public async Task CreateVaultAsync_UpdatesStateOnUiInvoker()
    {
        await _vm.CreateVaultAsync("StrongTestPassword123!", SecurityLevel.Standard);

        Assert.True(_uiInvokerCalls > 0);
        Assert.True(_vm.VaultExists);
        Assert.True(File.Exists(Path.Combine(_vaultDir, VaultConstants.VaultFileName)));
    }

    [Fact]
    public async Task UnlockAsync_MarshalsResultsThroughUiInvoker()
    {
        await _vm.CreateVaultAsync("StrongTestPassword123!", SecurityLevel.Standard);

        _vm.ResetForTesting();
        _vm.SetUiInvoker(action =>
        {
            _uiInvokerCalls++;
            action();
        });
        _vm.SwitchToPortableVault(_vaultDir);

        var (ok, error) = await _vm.UnlockAsync("StrongTestPassword123!");

        Assert.True(ok, error);
        Assert.True(_vm.IsUnlocked);
        Assert.True(_uiInvokerCalls > 0);
    }
}
