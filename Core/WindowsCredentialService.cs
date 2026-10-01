using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CredentialExplorer.Core;

public sealed class WindowsCredentialService
{
    public IReadOnlyList<CredentialMetadata> Enumerate()
    {
        // Flags zero preserves the API's target identity, rather than an ALL_CREDENTIALS namespace decoration.
        if (!NativeMethods.CredEnumerateW(null, 0, out var count, out var buffer))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 1168) return Array.Empty<CredentialMetadata>(); // Documented no-match result.
            throw new CredentialStoreException("Windows enumeration", error);
        }

        using var allocation = new CredentialBuffer(buffer);
        if (count != 0 && buffer == nint.Zero)
            throw new CredentialStoreException("Windows enumeration: invalid native buffer", 13);
        var entries = new List<CredentialMetadata>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            var pointer = Marshal.ReadIntPtr(buffer, checked((int)index * nint.Size));
            if (pointer == nint.Zero)
                throw new CredentialStoreException("Windows enumeration: invalid native record", 13);
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            entries.Add(ReadMetadata(native));
        }
        return entries;
    }

    public void Delete(CredentialMetadata credential)
    {
        if (credential.Store != CredentialStore.Windows || !credential.CanDelete)
            throw new CredentialStoreException("Windows removal: unsupported identity", 50);
        if (!NativeMethods.CredDeleteW(credential.Target, credential.NativeType, 0))
            throw new CredentialStoreException("Windows removal", Marshal.GetLastPInvokeError());
    }

    internal static CredentialMetadata ReadMetadata(NativeCredential native)
    {
        var target = Marshal.PtrToStringUni(native.TargetName);
        if (string.IsNullOrEmpty(target))
            throw new CredentialStoreException("Windows enumeration: missing target", 13);
        var fileTime = ((long)native.LastWrittenHigh << 32) | native.LastWrittenLow;
        DateTimeOffset? modified = null;
        if (fileTime != 0)
        {
            try { modified = new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime)); }
            catch (ArgumentOutOfRangeException)
            {
                throw new CredentialStoreException("Windows enumeration: invalid modification time", 13);
            }
        }
        return new CredentialMetadata
        {
            Store = CredentialStore.Windows,
            Target = target,
            UserName = Marshal.PtrToStringUni(native.UserName) ?? "",
            NativeType = native.Type,
            Modified = modified,
            Persistence = native.Persist
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    private sealed class CredentialBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CredentialBuffer(nint pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle()
        {
            NativeMethods.CredFree(handle);
            return true;
        }
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredEnumerateW(string? filter, uint flags, out uint count, out nint credentials);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern void CredFree(nint buffer);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDeleteW(string targetName, uint type, uint flags);
    }
}
