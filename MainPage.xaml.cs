using CredentialExplorer.Core;
using CredentialExplorer.Services;
using CredentialExplorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

namespace CredentialExplorer;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; }
    private bool initialized;

    public MainPage()
    {
        ICredentialService service = App.IsDemo
            ? new SyntheticCredentialService(App.DemoEmpty, App.DemoFailure)
            : new CredentialService();
        ViewModel = new(service, ConfirmRemovalAsync, App.IsDemo);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (initialized) return;
        initialized = true;
        StoreNavigation.SelectedItem = WindowsNavigation;
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void StoreChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!initialized || args.SelectedItem is not NavigationViewItem item) return;
        var store = Equals(item.Tag, "Web") ? CredentialStore.Web : CredentialStore.Windows;
        if (store == ViewModel.ActiveStore) return;
        if (store == CredentialStore.Web) await ViewModel.ShowWebCommand.ExecuteAsync(null);
        else await ViewModel.ShowWindowsCommand.ExecuteAsync(null);
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox picker) return;
        RequestedTheme = picker.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = RequestedTheme;
            App.Window.AppWindow.TitleBar.PreferredTheme = picker.SelectedIndex switch
            {
                1 => TitleBarTheme.Light,
                2 => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode
            };
        }
    }

    private void CredentialContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not CredentialMetadata entry) return;
        AutomationProperties.SetAutomationId(args.ItemContainer, $"CredentialRow{args.ItemIndex}");
        AutomationProperties.SetName(args.ItemContainer, $"{entry.Target}, {entry.DisplayUserName}, {entry.TypeName}");
    }

    private async Task<bool> ConfirmRemovalAsync(CredentialMetadata entry)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = $"Store: {entry.StoreName}\nTarget / resource: {entry.Target}\nUsername: {entry.DisplayUserName}\nType: {entry.TypeName}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Only this entry will be removed. You may need to sign back into its owning app. There is no undo.",
            TextWrapping = TextWrapping.Wrap
        });
        if (App.IsDemo)
            content.Children.Add(new TextBlock { Text = "Synthetic demo: only in-memory sample metadata can change.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = RequestedTheme,
            Title = "Remove this credential entry?",
            Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = "Remove entry",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        AutomationProperties.SetAutomationId(dialog, "RemovalConfirmation");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
