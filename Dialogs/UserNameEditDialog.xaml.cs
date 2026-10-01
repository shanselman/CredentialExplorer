using CredentialExplorer.Core;
using CredentialExplorer.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CredentialExplorer.Dialogs;

public sealed partial class UserNameEditDialog : ContentDialog
{
    public UserNameEditViewModel ViewModel { get; }
    public UserNameEditDialog(CredentialMetadata entry, bool isDemo)
    {
        ViewModel = new(entry, isDemo);
        InitializeComponent();
    }

    private void ValidateSave(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        args.Cancel = !ViewModel.CanSave;
}
