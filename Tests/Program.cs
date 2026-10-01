using System.Runtime.InteropServices;
using CredentialExplorer.Core;
using CredentialExplorer.ViewModels;
using CredentialExplorer.Services;
using CredentialExplorer.Tests;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}

var service = new SyntheticCredentialService();
var windows = service.Enumerate(CredentialStore.Windows);
var web = service.Enumerate(CredentialStore.Web);
Check(windows.Count == 163 && web.Count == 2, "Independent store counts");
Check(CredentialQuery.Apply(windows, " CALENDAR ", CredentialSort.TargetAscending).Count == 1, "Trimmed case-insensitive target search");
Check(CredentialQuery.Apply(windows, "sample-account", CredentialSort.UserName).Count == 1, "Username search");
Check(CredentialQuery.Apply(windows, "domain password", CredentialSort.Type).Count == 1, "Type search");
Check(CredentialQuery.Apply(windows, "", CredentialSort.TargetDescending)[0].Target == "Synthetic.Service/159", "Descending sort");
Check(CredentialQuery.Apply(windows, "", CredentialSort.RecentlyModified)[0].Target == "Example.Files/demo", "Modification sort with missing dates last");
Check(web.All(c => c.Modified is null && c.ModifiedText == "Not supplied by this API"), "Web metadata is not invented");
Check(!windows.Single(c => c.NativeType == 6).CanDelete, "Ambiguous extended deletion is disabled");
var unknown = new CredentialMetadata { Store = CredentialStore.Windows, Target = "synthetic-unknown", UserName = "", NativeType = 999 };
Check(!unknown.CanDelete, "Unknown types are read-only");

// Invalid blob pointers prove that projection never dereferences any secret, comment, or attribute field.
var target = Marshal.StringToHGlobalUni("Synthetic.Native/test");
var user = Marshal.StringToHGlobalUni("native-demo");
try
{
    var native = new WindowsCredentialService.NativeCredential
    {
        TargetName = target, UserName = user, Type = 1, Persist = 2,
        CredentialBlob = new nint(1), CredentialBlobSize = uint.MaxValue,
        Comment = new nint(1), Attributes = new nint(1), AttributeCount = uint.MaxValue
    };
    var projected = WindowsCredentialService.ReadMetadata(native);
    Check(projected.Target == "Synthetic.Native/test" && projected.UserName == "native-demo", "Native metadata projection ignores secret pointers");
    Check(Marshal.SizeOf<WindowsCredentialService.NativeCredential>() == (nint.Size == 8 ? 80 : 52), "CREDENTIALW ABI size");
    Check(Marshal.OffsetOf<WindowsCredentialService.NativeCredential>("UserName").ToInt32() == (nint.Size == 8 ? 72 : 48), "CREDENTIALW ABI username offset");
    native.LastWrittenHigh = uint.MaxValue;
    try { WindowsCredentialService.ReadMetadata(native); Check(false, "Invalid time rejected"); }
    catch (CredentialStoreException e) { Check(e.NativeCode == 13 && !e.Message.Contains("Synthetic.Native"), "Invalid metadata has redacted explicit error"); }
}
finally { Marshal.FreeHGlobal(target); Marshal.FreeHGlobal(user); }

var confirm = false;
var confirmations = 0;
var vm = new MainPageViewModel(service, _ => { confirmations++; return Task.FromResult(confirm); }, true);
await vm.RefreshCommand.ExecuteAsync(null);
Check(vm.CountText == "163 shown / 163 Windows entries enumerated", "ViewModel exact enumeration count");
vm.Selected = vm.Items[0];
vm.SearchText = "does-not-exist";
Check(vm.Items.Count == 0 && vm.Selected is null && vm.ShowEmpty, "Filtering clears hidden selection");
Check(vm.CountText == "0 shown / 163 Windows entries enumerated", "Filtering does not change store count");
vm.SearchText = "calendar";
vm.Selected = vm.Items.Single();
await vm.RemoveCommand.ExecuteAsync(null);
Check(confirmations == 1 && service.DeleteCalls == 0 && vm.Selected is not null, "Cancel never calls removal");
confirm = true;
await vm.RemoveCommand.ExecuteAsync(null);
Check(service.DeleteCalls == 1 && vm.CountText == "0 shown / 162 Windows entries enumerated" && vm.Selected is null, "Confirmed synthetic removal refreshes actual count");
await vm.ShowWebCommand.ExecuteAsync(null);
Check(vm.CountText == "2 shown / 2 Credential Locker entries enumerated" && vm.SearchText == "", "Web switch clears search and isolates counts");
Check(vm.WindowsLabel == "Windows (162)" && vm.WebLabel == "Web / Credential Locker (2)", "Independent navigation counts");

var failure = new MainPageViewModel(new SyntheticCredentialService(failure: true), _ => Task.FromResult(true), true);
await failure.RefreshCommand.ExecuteAsync(null);
Check(failure.HasError && failure.Items.Count == 0 && failure.CountText.Contains("unavailable"), "Failure is not empty success");
Check(!failure.RemoveCommand.CanExecute(null) && !failure.IsBusy, "Failure leaves actions safe and refresh usable");
var empty = new MainPageViewModel(new SyntheticCredentialService(empty: true), _ => Task.FromResult(false), true);
await empty.RefreshCommand.ExecuteAsync(null);
Check(!empty.HasError && empty.CountText == "0 shown / 0 Windows entries enumerated" && empty.ShowEmpty, "Successful empty enumeration has accurate zero count");
Check(new CredentialStoreException("Synthetic operation", 8).Message.Contains("does not establish a store capacity"), "Error 8 does not invent limits");

var editable = windows.Single(c => c.Target == "Example.Calendar/demo");
var domain = windows.Single(c => c.NativeType == 2);
Check(editable.CanEditUserName && domain.CanEditUserName && web.All(c => !c.CanEditUserName) && !unknown.CanEditUserName,
    "Username editing is scoped to supported Windows types");
Check(CredentialEdits.GetValidationError(editable, "") is null &&
    CredentialEdits.GetValidationError(domain, "") is not null, "Blank usernames follow credential type rules");
Check(CredentialEdits.GetValidationError(editable, "demo\0suffix") is not null &&
    CredentialEdits.GetValidationError(editable, new string('x', 514)) is not null, "Invalid and overlong usernames rejected");
var editVm = new UserNameEditViewModel(domain);
Check(!editVm.CanSave, "Unchanged username cannot be saved");
editVm.UserName = "";
Check(!editVm.CanSave && editVm.HasValidationError, "Editor surfaces domain username validation");
editVm.UserName = "new-domain-demo";
Check(editVm.CanSave && !editVm.HasValidationError, "Editor permits valid changed username");

var fake = new FakeMetadataApi(editable);
new WindowsCredentialService(fake).UpdateUserName(editable, "edited-native-demo");
Check(fake.WriteCalls == 1 && fake.WriteFlags == WindowsCredentialService.PreserveCredentialBlob,
    "Native update uses CRED_PRESERVE_CREDENTIAL_BLOB");
Check(fake.FieldsPreserved && fake.BufferAliveAtWrite && fake.FreeCalls == 1,
    "All other native fields preserved and fresh buffer released exactly once");
Check(fake.WrittenUserName == "edited-native-demo" && fake.RequestedTarget == editable.Target && fake.RequestedType == editable.NativeType,
    "Native update uses exact target/type and only the new username");

var staleApi = new FakeMetadataApi(new CredentialMetadata
{
    Store = editable.Store, Target = editable.Target, NativeType = editable.NativeType,
    UserName = "externally-changed-demo", Modified = editable.Modified, Persistence = editable.Persistence
});
try { new WindowsCredentialService(staleApi).UpdateUserName(editable, "edited-native-demo"); Check(false, "Stale entry rejected"); }
catch (CredentialStoreException e) { Check(e.NativeCode == 1306 && staleApi.WriteCalls == 0 && staleApi.FreeCalls == 1, "Stale snapshot rejected before writing with native buffer released"); }
var writeFailure = new FakeMetadataApi(editable) { WriteError = 8 };
try { new WindowsCredentialService(writeFailure).UpdateUserName(editable, "edited-native-demo"); Check(false, "Write failure surfaced"); }
catch (CredentialStoreException e) { Check(e.NativeCode == 8 && writeFailure.FreeCalls == 1 && !e.Message.Contains(editable.Target), "Write errors remain redacted and release buffers"); }
var missing = new FakeMetadataApi(editable) { ReadError = 1168 };
try { new WindowsCredentialService(missing).UpdateUserName(editable, "edited-native-demo"); Check(false, "Missing entry rejected"); }
catch (CredentialStoreException e) { Check(e.NativeCode == 1168 && missing.WriteCalls == 0 && missing.FreeCalls == 0, "Missing entry is never recreated"); }
var noChange = new FakeMetadataApi(editable);
new WindowsCredentialService(noChange).UpdateUserName(editable, editable.UserName);
Check(noChange.WriteCalls == 0 && noChange.FreeCalls == 1, "No-op native update performs no write");
var unsupported = new FakeMetadataApi(web[0]);
try { new WindowsCredentialService(unsupported).UpdateUserName(web[0], "edited-demo"); Check(false, "Web update rejected"); }
catch (CredentialStoreException e) { Check(e.NativeCode == 87 && unsupported.ReadCalls == 0, "Unsupported store rejected before any native access"); }

var editService = new SyntheticCredentialService();
string? editorResult = null;
var editFlow = new MainPageViewModel(editService, _ => Task.FromResult(false), true, _ => Task.FromResult(editorResult));
await editFlow.RefreshCommand.ExecuteAsync(null);
editFlow.SearchText = "calendar";
editFlow.Selected = editFlow.Items.Single();
await editFlow.EditUserNameCommand.ExecuteAsync(null);
Check(editService.UpdateCalls == 0 && !editFlow.IsBusy, "Cancel editor never writes");
editorResult = "edited-demo";
await editFlow.EditUserNameCommand.ExecuteAsync(null);
Check(editService.UpdateCalls == 1 && editFlow.Selected?.UserName == "edited-demo" &&
    editFlow.CountText == "1 shown / 163 Windows entries enumerated", "Confirmed synthetic edit preserves identity and store count");
Check(editFlow.Selected?.Persistence == editable.Persistence && editFlow.Selected?.NativeType == editable.NativeType,
    "Synthetic edits preserve type and persistence");
editFlow.SearchText = "edited-demo";
editFlow.Selected = editFlow.Items.Single();
editorResult = "another-demo";
await editFlow.EditUserNameCommand.ExecuteAsync(null);
Check(editFlow.Selected is null && editFlow.Items.Count == 0 && editFlow.CountText == "0 shown / 163 Windows entries enumerated",
    "Edited usernames leaving the search clear the selection without changing store count");
await editFlow.ShowWebCommand.ExecuteAsync(null);
editFlow.Selected = editFlow.Items[0];
Check(!editFlow.EditUserNameCommand.CanExecute(null), "Web username editing stays disabled");
editFlow.DensityIndex = 1;
Check(editFlow.IsCompact, "Compact density state is observable");

Console.WriteLine($"{passed} assertions passed. No native credential store was accessed.");

if (args.Contains("--native-readonly", StringComparer.Ordinal))
{
    var nativeService = new CredentialService();
    foreach (var store in Enum.GetValues<CredentialStore>())
    {
        try
        {
            _ = nativeService.Enumerate(store);
            Console.WriteLine($"Native {store} metadata enumeration succeeded. All metadata and counts are redacted.");
        }
        catch (CredentialStoreException error)
        {
            Console.WriteLine($"Native {store} enumeration reported an explicit unavailable state: {error.Message}");
        }
    }
    Console.WriteLine("Read-only native checks finished. No native writes, secret reads, or deletions were performed.");
}
