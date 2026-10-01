using CommunityToolkit.Mvvm.ComponentModel;
using CredentialExplorer.Core;

namespace CredentialExplorer.ViewModels;

public partial class UserNameEditViewModel(CredentialMetadata entry, bool isDemo = false) : ObservableObject
{
    public CredentialMetadata Entry { get; } = entry;
    public bool IsDemo { get; } = isDemo;
    public string Target => Entry.Target;
    public string TypeName => Entry.TypeName;
    public string EditNote => Entry.EditNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string UserName { get; set; } = entry.UserName;

    public string ValidationError => CredentialEdits.GetValidationError(Entry, UserName) ?? "";
    public bool HasValidationError => ValidationError.Length != 0;
    public bool CanSave => !HasValidationError && !string.Equals(UserName, Entry.UserName, StringComparison.Ordinal);
}
