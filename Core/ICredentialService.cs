namespace CredentialExplorer.Core;

public interface ICredentialService
{
    IReadOnlyList<CredentialMetadata> Enumerate(CredentialStore store);
    void Delete(CredentialMetadata credential);
    void UpdateUserName(CredentialMetadata credential, string userName);
}

public sealed class CredentialStoreException : Exception
{
    public CredentialStoreException(string operation, int code, bool isHResult = false)
        : base($"{operation} failed ({(isHResult ? $"HRESULT 0x{unchecked((uint)code):X8}" : $"Win32 {code}")}). " +
            (!isHResult && code == 8
                ? "Windows reported ERROR_NOT_ENOUGH_MEMORY. This does not establish a store capacity or RAM usage."
                : "No credential values were logged. Try Refresh; the store may be unavailable in this session."))
    {
        NativeCode = code;
        IsHResult = isHResult;
    }

    public int NativeCode { get; }
    public bool IsHResult { get; }
}
