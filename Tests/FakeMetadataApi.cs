using System.Runtime.InteropServices;
using System.Text;
using CredentialExplorer.Core;

namespace CredentialExplorer.Tests;

internal sealed class FakeMetadataApi(CredentialMetadata entry) : WindowsCredentialService.IMetadataApi
{
    public int ReadError { get; init; }
    public int WriteError { get; init; }
    public int ReadCalls { get; private set; }
    public int WriteCalls { get; private set; }
    public int FreeCalls { get; private set; }
    public bool BufferAliveAtWrite { get; private set; }
    public bool FieldsPreserved { get; private set; }
    public uint WriteFlags { get; private set; }
    public string? WrittenUserName { get; private set; }
    public string? RequestedTarget { get; private set; }
    public uint RequestedType { get; private set; }
    private nint allocation;
    private WindowsCredentialService.NativeCredential original;

    public int Read(string target, uint type, out nint buffer)
    {
        ReadCalls++;
        RequestedTarget = target;
        RequestedType = type;
        buffer = nint.Zero;
        if (ReadError != 0) return ReadError;
        var targetBytes = Encoding.Unicode.GetBytes(entry.Target + "\0");
        var userBytes = Encoding.Unicode.GetBytes(entry.UserName + "\0");
        var size = Marshal.SizeOf<WindowsCredentialService.NativeCredential>();
        allocation = Marshal.AllocHGlobal(size + targetBytes.Length + userBytes.Length);
        var targetPointer = allocation + size;
        var userPointer = targetPointer + targetBytes.Length;
        Marshal.Copy(targetBytes, 0, targetPointer, targetBytes.Length);
        Marshal.Copy(userBytes, 0, userPointer, userBytes.Length);
        var time = entry.Modified?.UtcDateTime.ToFileTimeUtc() ?? 0;
        original = new()
        {
            TargetName = targetPointer, UserName = userPointer, Type = entry.NativeType,
            Persist = entry.Persistence ?? 0, Flags = 4,
            LastWrittenLow = unchecked((uint)time), LastWrittenHigh = unchecked((uint)(time >> 32)),
            // Invalid opaque pointers ensure the managed edit path never inspects secret or app-defined contents.
            CredentialBlob = new nint(1), CredentialBlobSize = uint.MaxValue,
            Comment = new nint(2), TargetAlias = new nint(3), Attributes = new nint(4), AttributeCount = 16
        };
        Marshal.StructureToPtr(original, allocation, false);
        buffer = allocation;
        return 0;
    }

    public int Write(ref WindowsCredentialService.NativeCredential credential, uint flags)
    {
        WriteCalls++;
        WriteFlags = flags;
        BufferAliveAtWrite = allocation != nint.Zero;
        WrittenUserName = Marshal.PtrToStringUni(credential.UserName);
        FieldsPreserved = credential.TargetName == original.TargetName && credential.Type == original.Type &&
            credential.Flags == original.Flags && credential.Persist == original.Persist &&
            credential.Comment == original.Comment && credential.TargetAlias == original.TargetAlias &&
            credential.Attributes == original.Attributes && credential.AttributeCount == original.AttributeCount &&
            credential.LastWrittenLow == original.LastWrittenLow && credential.LastWrittenHigh == original.LastWrittenHigh &&
            credential.CredentialBlobSize == 0 && credential.CredentialBlob == nint.Zero;
        return WriteError;
    }

    public void Free(nint buffer)
    {
        if (buffer != allocation || allocation == nint.Zero) throw new InvalidOperationException("Invalid synthetic buffer release.");
        Marshal.FreeHGlobal(allocation);
        allocation = nint.Zero;
        FreeCalls++;
    }
}
