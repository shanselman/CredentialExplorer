using System.Runtime.InteropServices;
using CredentialExplorer.Core;
using Windows.Security.Credentials;

namespace CredentialExplorer.Services;

public sealed class CredentialService : ICredentialService
{
    private readonly WindowsCredentialService windows = new();

    public IReadOnlyList<CredentialMetadata> Enumerate(CredentialStore store)
    {
        if (store == CredentialStore.Windows) return windows.Enumerate();
        try
        {
            var vault = new PasswordVault();
            return vault.RetrieveAll().Select(c => new CredentialMetadata
            {
                Store = CredentialStore.Web,
                Target = c.Resource,
                UserName = c.UserName
            }).ToArray();
        }
        catch (COMException error) { throw new CredentialStoreException("Credential Locker enumeration", error.HResult, true); }
        catch (UnauthorizedAccessException error) { throw new CredentialStoreException("Credential Locker enumeration", error.HResult, true); }
        catch (InvalidOperationException error) { throw new CredentialStoreException("Credential Locker enumeration", error.HResult, true); }
    }

    public void Delete(CredentialMetadata credential)
    {
        if (credential.Store == CredentialStore.Windows) { windows.Delete(credential); return; }
        if (!credential.CanDelete)
            throw new CredentialStoreException("Credential Locker removal: unsupported identity", 50);
        try
        {
            var vault = new PasswordVault();
            // Match metadata from a fresh snapshot; never Retrieve(), RetrievePassword(), or read Password.
            var matches = vault.RetrieveAll().Where(c =>
                string.Equals(c.Resource, credential.Target, StringComparison.Ordinal) &&
                string.Equals(c.UserName, credential.UserName, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0) throw new CredentialStoreException("Credential Locker removal", 1168);
            if (matches.Length != 1) throw new CredentialStoreException("Credential Locker removal: ambiguous identity", 13);
            vault.Remove(matches[0]);
        }
        catch (COMException error) { throw new CredentialStoreException("Credential Locker removal", error.HResult, true); }
        catch (UnauthorizedAccessException error) { throw new CredentialStoreException("Credential Locker removal", error.HResult, true); }
        catch (InvalidOperationException error) { throw new CredentialStoreException("Credential Locker removal", error.HResult, true); }
    }
}
