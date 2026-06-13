using Fortiva.AppHost.Pages;

using Fortiva.AppHost.ViewModels;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Input;

using Windows.System;



namespace Fortiva.AppHost.Services;



public sealed class CommandPaletteItem

{

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public required string Glyph { get; init; }

    public required string Category { get; init; }

    public required Func<Task> ExecuteAsync { get; init; }

    public string SearchText => $"{Title} {Subtitle} {Category}".ToLowerInvariant();

}



public static class CommandPalette

{

    public static Task ShowAsync(XamlRoot xamlRoot, ShellViewModel vm)

    {

        if (!vm.IsUnlocked || vm.IsAdmin)

            return Task.CompletedTask;



        return ShowCoreAsync(xamlRoot, vm);

    }



    private static async Task ShowCoreAsync(XamlRoot xamlRoot, ShellViewModel vm)

    {

        var items = BuildItems(vm);

        var box = new AutoSuggestBox

        {

            PlaceholderText = "Search entries and commands…",

            Style = Application.Current.Resources["FortivaSearchBox"] as Style

        };



        var list = new ListView

        {

            SelectionMode = ListViewSelectionMode.Single,

            MaxHeight = 360,

            IsItemClickEnabled = true

        };



        void Render(string? query)

        {

            var q = query?.Trim().ToLowerInvariant() ?? "";

            var filtered = string.IsNullOrEmpty(q)

                ? items

                : items.Where(i => i.SearchText.Contains(q, StringComparison.Ordinal)).ToList();

            list.ItemsSource = filtered;

            if (filtered.Count > 0)

                list.SelectedIndex = 0;

        }



        list.ItemTemplate = (DataTemplate)Application.Current.Resources["CommandPaletteItemTemplate"];

        Render(null);



        box.TextChanged += (_, args) =>

        {

            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)

                Render(box.Text);

        };



        var root = new StackPanel { Spacing = 12, MinWidth = 480 };

        root.Children.Add(box);

        root.Children.Add(list);



        var dialog = new ContentDialog

        {

            Title = "Command palette",

            Content = root,

            CloseButtonText = "Close",

            DefaultButton = ContentDialogButton.Close,

            XamlRoot = xamlRoot

        };

        FortivaDialogs.Configure(dialog, xamlRoot);



        async Task RunSelectedAsync()

        {

            if (list.SelectedItem is not CommandPaletteItem item)

                return;



            dialog.Hide();

            try

            {

                await item.ExecuteAsync();

            }

            catch (Exception ex)

            {

                vm.StatusMessage = $"Command failed: {ex.Message}";

            }

        }



        list.ItemClick += async (_, _) => await RunSelectedAsync();

        box.QuerySubmitted += async (_, _) => await RunSelectedAsync();

        list.KeyDown += async (_, e) =>

        {

            if (e.Key == VirtualKey.Enter)

            {

                e.Handled = true;

                await RunSelectedAsync();

            }

        };

        box.KeyDown += (_, e) =>

        {

            if (e.Key == VirtualKey.Down && list.Items.Count > 0)

            {

                e.Handled = true;

                list.Focus(FocusState.Programmatic);

            }

        };



        await dialog.ShowAsync();

        box.Focus(FocusState.Programmatic);

        Render(box.Text);

    }



    private static List<CommandPaletteItem> BuildItems(ShellViewModel vm)

    {

        var items = new List<CommandPaletteItem>();



        foreach (var entry in vm.Entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))

        {

            var entryId = entry.Id;

            items.Add(new CommandPaletteItem

            {

                Title = entry.Title,

                Subtitle = entry.Subtitle,

                Glyph = "\uE8D7",

                Category = "Entry",

                ExecuteAsync = async () =>

                {

                    vm.RequestNavigationTab("Vault");

                    NavigationService.Current.Navigate<VaultPage>(VaultPageNavigationContext.ForEntry(entryId));

                    await Task.CompletedTask;

                }

            });

        }



        items.Add(new CommandPaletteItem

        {

            Title = "New entry",

            Subtitle = "Quick add dialog",

            Glyph = "\uE710",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.RequestNavigationTab("Vault");

                NavigationService.Current.Navigate<VaultPage>(VaultPageNavigationContext.ForQuickAdd());

                await Task.CompletedTask;

            }

        });



        items.Add(new CommandPaletteItem

        {

            Title = "Password generator",

            Glyph = "\uE9D5",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.RequestNavigationTab("Generator");

                NavigationService.Current.Navigate<PasswordGeneratorPage>();

                await Task.CompletedTask;

            }

        });



        items.Add(new CommandPaletteItem

        {

            Title = "Security audit",

            Glyph = "\uE946",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.RequestNavigationTab("Health");

                NavigationService.Current.Navigate<HealthPage>();

                await Task.CompletedTask;

            }

        });



        items.Add(new CommandPaletteItem

        {

            Title = "Settings",

            Glyph = "\uE713",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.RequestNavigationTab("Settings");

                NavigationService.Current.Navigate<SettingsPage>();

                await Task.CompletedTask;

            }

        });



        items.Add(new CommandPaletteItem

        {

            Title = "Import / Export",

            Glyph = "\uE8B5",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.RequestNavigationTab("ImportExport");

                NavigationService.Current.Navigate<ImportExportPage>();

                await Task.CompletedTask;

            }

        });



        items.Add(new CommandPaletteItem

        {

            Title = "Lock vault",

            Glyph = "\uE72E",

            Category = "Action",

            ExecuteAsync = async () =>

            {

                vm.Lock();

                await Task.CompletedTask;

            }

        });



        return items;

    }

}


