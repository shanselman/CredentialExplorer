using System.Runtime.InteropServices;
using CredentialExplorer.Core;
using CredentialExplorer.ViewModels;
using CredentialExplorer.Services;

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
