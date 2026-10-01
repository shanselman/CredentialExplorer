namespace CredentialExplorer.Core;

public sealed class SyntheticCredentialService : ICredentialService
{
    private readonly List<CredentialMetadata> entries;
    private readonly bool failEnumeration;
    public int EnumerationCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int UpdateCalls { get; private set; }

    public SyntheticCredentialService(bool empty = false, bool failure = false)
    {
        failEnumeration = failure;
        entries = empty ? [] :
        [
            new() { Store = CredentialStore.Windows, Target = "Example.Calendar/demo", UserName = "demo-user", NativeType = 1,
                Persistence = 2, Modified = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero) },
            new() { Store = CredentialStore.Windows, Target = "Example.Files/demo", UserName = "sample-account", NativeType = 2,
                Persistence = 3, Modified = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero) },
            new() { Store = CredentialStore.Windows, Target = "Example.Extended/demo", UserName = "demo-user", NativeType = 6 },
            new() { Store = CredentialStore.Web, Target = "https://example.invalid", UserName = "web-demo" },
            new() { Store = CredentialStore.Web, Target = "Example Locker App", UserName = "demo-user" }
        ];
        if (!empty)
        {
            for (var i = 0; i < 160; i++)
                entries.Add(new() { Store = CredentialStore.Windows, Target = $"Synthetic.Service/{i:D3}",
                    UserName = "virtualization-demo", NativeType = 1, Persistence = 1 });
        }
    }

    public IReadOnlyList<CredentialMetadata> Enumerate(CredentialStore store)
    {
        EnumerationCalls++;
        if (failEnumeration) throw new CredentialStoreException("Synthetic enumeration", 5);
        return entries.Where(c => c.Store == store).ToArray();
    }

    public void Delete(CredentialMetadata credential)
    {
        if (!credential.CanDelete) throw new CredentialStoreException("Synthetic removal: unsupported type", 50);
        var entry = entries.SingleOrDefault(c => c.HasSameIdentity(credential));
        if (entry is null) throw new CredentialStoreException("Synthetic removal", 1168);
        entries.Remove(entry);
        DeleteCalls++;
    }

    public void UpdateUserName(CredentialMetadata credential, string userName)
    {
        CredentialEdits.Validate(credential, userName);
        var entry = entries.SingleOrDefault(c => c.HasSameIdentity(credential));
        if (entry is null) throw new CredentialStoreException("Synthetic username update", 1168);
        CredentialEdits.EnsureUnchanged(credential, entry);
        if (string.Equals(entry.UserName, userName, StringComparison.Ordinal)) return;
        entries[entries.IndexOf(entry)] = new CredentialMetadata
        {
            Store = entry.Store, Target = entry.Target, NativeType = entry.NativeType,
            UserName = userName, Persistence = entry.Persistence, Modified = DateTimeOffset.UtcNow
        };
        UpdateCalls++;
    }
}
