using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CredentialExplorer.Core;

namespace CredentialExplorer.ViewModels;

public partial class MainPageViewModel(
    ICredentialService service, Func<CredentialMetadata, Task<bool>> confirmRemoval, bool isDemo) : ObservableObject
{
    private IReadOnlyList<CredentialMetadata> snapshot = [];
    private int? windowsCount;
    private int? webCount;
    public bool IsDemo { get; } = isDemo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowWindowsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowWebCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyPropertyChangedFor(nameof(IsInteractive))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial CredentialStore ActiveStore { get; set; } = CredentialStore.Windows;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CredentialMetadata> Items { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(DetailTarget))]
    [NotifyPropertyChangedFor(nameof(DetailUserName))]
    [NotifyPropertyChangedFor(nameof(DetailType))]
    [NotifyPropertyChangedFor(nameof(DetailModified))]
    [NotifyPropertyChangedFor(nameof(DetailPersistence))]
    [NotifyPropertyChangedFor(nameof(DetailRemovalNote))]
    public partial CredentialMetadata? Selected { get; set; }

    [ObservableProperty]
    public partial string Heading { get; set; } = "Windows credentials";

    [ObservableProperty]
    public partial string ScopeNote { get; set; } = "";

    [ObservableProperty]
    public partial string CountText { get; set; } = "Not enumerated";

    [ObservableProperty]
    public partial string WindowsLabel { get; set; } = "Windows";

    [ObservableProperty]
    public partial string WebLabel { get; set; } = "Web / Credential Locker";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowEmpty { get; set; }

    public bool IsInteractive => !IsBusy;
    public bool HasSelection => Selected is not null;
    public string DetailTarget => Selected?.Target ?? "";
    public string DetailUserName => Selected?.DisplayUserName ?? "";
    public string DetailType => Selected?.TypeName ?? "";
    public string DetailModified => Selected?.ModifiedText ?? "";
    public string DetailPersistence => Selected?.PersistenceText ?? "";
    public string DetailRemovalNote => Selected?.RemovalNote ?? "";

    partial void OnSearchTextChanged(string value) => ApplyQuery();
    partial void OnSortIndexChanged(int value) => ApplyQuery();

    private bool CanInteract() => !IsBusy;
    private bool CanRemove() => !IsBusy && Selected?.CanDelete == true;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ShowWindowsAsync() => await SwitchAsync(CredentialStore.Windows);

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ShowWebAsync() => await SwitchAsync(CredentialStore.Web);

    private async Task SwitchAsync(CredentialStore store)
    {
        ActiveStore = store;
        SearchText = "";
        Selected = null;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Reading metadata...";
        SetScope();
        try { await LoadAsync(); }
        finally { IsBusy = false; }
    }

    private void SetScope()
    {
        Heading = ActiveStore == CredentialStore.Windows ? "Windows credentials" : "Web / Credential Locker";
        ScopeNote = ActiveStore == CredentialStore.Windows
            ? "Current logon credential set via CredEnumerateW. Counts describe enumerated entries, not capacity. No app ownership is inferred."
            : "Supported Credential Locker metadata via PasswordVault. Legacy Web Credentials coverage may differ. Not Edge, Chrome, Firefox, or other browser password databases. This API does not supply modification time or owning app.";
        if (IsDemo) ScopeNote = "SYNTHETIC DEMO - no host stores are accessed. " + ScopeNote;
    }

    private async Task<bool> LoadAsync()
    {
        var previous = Selected;
        Selected = null;
        snapshot = [];
        Items = [];
        CountText = "Reading metadata...";
        HasError = false;
        ErrorText = "";
        ShowEmpty = false;
        try
        {
            snapshot = await Task.Run(() => service.Enumerate(ActiveStore));
            SetCount(snapshot.Count);
            ApplyQuery();
            Selected = previous is null ? null : Items.FirstOrDefault(c => c.HasSameIdentity(previous));
            StatusText = IsDemo ? "Synthetic metadata only. Nothing is written to Windows." : "Metadata only. Secrets are never displayed, copied, exported, or logged.";
            return true;
        }
        catch (CredentialStoreException error)
        {
            SetCount(null);
            HasError = true;
            ErrorText = error.Message;
            CountText = "Count unavailable - enumeration did not succeed";
            StatusText = "Store unavailable. Refresh to retry.";
            EmptyText = "This store could not be enumerated. This is not an empty-store result.";
            ShowEmpty = true;
            return false;
        }
    }

    private void SetCount(int? count)
    {
        if (ActiveStore == CredentialStore.Windows) windowsCount = count;
        else webCount = count;
        WindowsLabel = windowsCount is { } w ? $"Windows ({w})" : "Windows (not enumerated)";
        WebLabel = webCount is { } v ? $"Web / Credential Locker ({v})" : "Web / Credential Locker (not enumerated)";
    }

    private void ApplyQuery()
    {
        var previous = Selected;
        Items = CredentialQuery.Apply(snapshot, SearchText, (CredentialSort)SortIndex);
        Selected = previous is null ? null : Items.FirstOrDefault(c => c.HasSameIdentity(previous));
        if (HasError) return;
        var noun = ActiveStore == CredentialStore.Windows ? "Windows entries" : "Credential Locker entries";
        CountText = $"{Items.Count} shown / {snapshot.Count} {noun} enumerated";
        EmptyText = snapshot.Count == 0 ? "No entries were returned by this store's API." : "No metadata matches your search.";
        ShowEmpty = Items.Count == 0;
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        var entry = Selected;
        if (entry is null || !entry.CanDelete || IsBusy) return;
        IsBusy = true;
        try
        {
            if (!await confirmRemoval(entry))
            {
                StatusText = "Removal canceled. No credential was changed.";
                return;
            }
            HasError = false;
            ErrorText = "";
            try { await Task.Run(() => service.Delete(entry)); }
            catch (CredentialStoreException error)
            {
                HasError = true;
                ErrorText = error.Message;
                StatusText = "Removal failed. Refresh to check the current store.";
                return;
            }
            Selected = null;
            var refreshed = await LoadAsync();
            StatusText = refreshed
                ? "Entry removed. You may need to sign back into its owning app."
                : "Entry removed, but refreshing the store failed. The count is unavailable.";
        }
        finally { IsBusy = false; }
    }
}
