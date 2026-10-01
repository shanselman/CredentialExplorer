using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CredentialExplorer.Core;

public sealed class WindowsCredentialService
{
    internal const uint PreserveCredentialBlob = 1;
    private readonly IMetadataApi editor;

    public WindowsCredentialService() : this(new MetadataApi()) { }
    internal WindowsCredentialService(IMetadataApi editor) => this.editor = editor;

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

    public void UpdateUserName(CredentialMetadata credential, string userName)
    {
        CredentialEdits.Validate(credential, userName);
        var error = editor.Read(credential.Target, credential.NativeType, out var buffer);
        if (error != 0) throw new CredentialStoreException("Windows username update: read current entry", error);
        using var allocation = new CredentialBuffer(buffer, editor.Free);
        if (buffer == nint.Zero)
            throw new CredentialStoreException("Windows username update: invalid native record", 13);
        var current = Marshal.PtrToStructure<NativeCredential>(buffer);
        CredentialEdits.EnsureUnchanged(credential, ReadMetadata(current));
        if (string.Equals(credential.UserName, userName, StringComparison.Ordinal)) return;
        var userNamePointer = Marshal.StringToHGlobalUni(userName);
        try
        {
            // Retain the fresh native record's flags, comments, alias, attributes and persistence without projecting their contents.
            current.UserName = userNamePointer;
            current.CredentialBlobSize = 0;
            current.CredentialBlob = nint.Zero;
            error = editor.Write(ref current, PreserveCredentialBlob);
            if (error != 0) throw new CredentialStoreException("Windows username update", error);
        }
        finally { Marshal.FreeHGlobal(userNamePointer); }
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

    internal interface IMetadataApi
    {
        int Read(string target, uint type, out nint buffer);
        int Write(ref NativeCredential credential, uint flags);
        void Free(nint buffer);
    }

    private sealed class MetadataApi : IMetadataApi
    {
        public int Read(string target, uint type, out nint buffer) =>
            NativeMethods.CredReadW(target, type, 0, out buffer) ? 0 : Marshal.GetLastPInvokeError();
        public int Write(ref NativeCredential credential, uint flags) =>
            NativeMethods.CredWriteW(ref credential, flags) ? 0 : Marshal.GetLastPInvokeError();
        public void Free(nint buffer) => NativeMethods.CredFree(buffer);
    }

    private sealed class CredentialBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Action<nint> release;
        public CredentialBuffer(nint pointer, Action<nint>? release = null) : base(true)
        {
            this.release = release ?? NativeMethods.CredFree;
            SetHandle(pointer);
        }
        protected override bool ReleaseHandle()
        {
            release(handle);
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

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredReadW(string targetName, uint type, uint flags, out nint credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWriteW(ref NativeCredential credential, uint flags);
    }
}
