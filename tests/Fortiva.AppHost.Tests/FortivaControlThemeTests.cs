using Fortiva.AppHost.Services;
using Fortiva.AppHost.ViewModels;
using Xunit;

namespace Fortiva.AppHost.Tests;

public sealed class FortivaControlThemeTests
{
    [Fact]
    public void ResolveAppTheme_UsesExplicitLightPreference()
    {
        var vm = ShellViewModel.Current;
        var previous = vm.ThemePreference;
        try
        {
            vm.SetThemePreference(AppThemePreference.Light);
            Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Light, FortivaControlTheme.ResolveAppTheme());
        }
        finally
        {
            vm.SetThemePreference(previous);
        }
    }

    [Fact]
    public void ResolveAppTheme_UsesExplicitDarkPreference()
    {
        var vm = ShellViewModel.Current;
        var previous = vm.ThemePreference;
        try
        {
            vm.SetThemePreference(AppThemePreference.Dark);
            Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, FortivaControlTheme.ResolveAppTheme());
        }
        finally
        {
            vm.SetThemePreference(previous);
        }
    }
}
