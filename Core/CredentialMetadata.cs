namespace CredentialExplorer.Core;

public enum CredentialStore { Windows, Web }
public enum CredentialSort { TargetAscending, TargetDescending, UserName, Type, RecentlyModified }

public sealed class CredentialMetadata
{
    public required CredentialStore Store { get; init; }
    public required string Target { get; init; }
    public required string UserName { get; init; }
    public uint NativeType { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public uint? Persistence { get; init; }

    public string StoreName => Store == CredentialStore.Windows ? "Windows credentials" : "Web / Credential Locker";
    public string TypeName => Store == CredentialStore.Web ? "Credential Locker" : NativeType switch
    {
        1 => "Generic",
        2 => "Domain password",
        3 => "Domain certificate",
        4 => "Domain visible password (unsupported)",
        5 => "Generic certificate",
        6 => "Domain extended",
        _ => $"Unknown type ({NativeType})"
    };
    public string DisplayUserName => string.IsNullOrEmpty(UserName) ? "Not supplied" : UserName;
    public string ModifiedText => Modified?.ToLocalTime().ToString("g") ?? "Not supplied by this API";
    public string ModifiedShortText => Modified?.ToLocalTime().ToString("d") ?? "Not supplied";
    public string IconGlyph => Store == CredentialStore.Web ? "\uE774" : "\uE8D7";
    public string PersistenceText => Persistence switch
    {
        1 => "Logon session",
        2 => "Local machine",
        3 => "Enterprise (roaming depends on policy)",
        null => "Not supplied by this API",
        _ => $"Unknown ({Persistence})"
    };
    public bool CanDelete => !string.IsNullOrEmpty(Target) &&
        (Store == CredentialStore.Web || NativeType is 1 or 2 or 3 or 5);
    public bool CanEditUserName => Store == CredentialStore.Windows &&
        !string.IsNullOrEmpty(Target) && NativeType is 1 or 2;
    public string EditNote => CanEditUserName
        ? NativeType == 1
            ? "Username is editable metadata. Generic secrets are app-defined; changing this does not change a password or guarantee the app will use it."
            : "Username changes can affect automatic sign-in. The existing password stays unchanged."
        : Store == CredentialStore.Web
            ? "Username editing is unavailable: Credential Locker has no supported metadata-only update operation."
            : "Username editing is available only for Generic and Domain password entries. Certificate, extended, and unknown types are read-only.";
    public string RemovalNote => CanDelete
        ? "Remove only an entry you recognize. Its owning app may require you to sign in again."
        : "Removal is unavailable for this type: the MVP does not infer extended or unknown deletion identities.";

    public bool HasSameIdentity(CredentialMetadata other) =>
        Store == other.Store && NativeType == other.NativeType &&
        string.Equals(Target, other.Target, Store == CredentialStore.Windows
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        (Store == CredentialStore.Windows && NativeType != 6 ||
         string.Equals(UserName, other.UserName, StringComparison.Ordinal));
}

public static class CredentialQuery
{
    public static IReadOnlyList<CredentialMetadata> Apply(
        IEnumerable<CredentialMetadata> source, string query, CredentialSort sort)
    {
        var term = query.Trim();
        var filtered = source.Where(c => term.Length == 0 ||
            c.Target.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.UserName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.TypeName.Contains(term, StringComparison.OrdinalIgnoreCase));
        var comparer = StringComparer.OrdinalIgnoreCase;
        return (sort switch
        {
            CredentialSort.TargetDescending => filtered.OrderByDescending(c => c.Target, comparer),
            CredentialSort.UserName => filtered.OrderBy(c => c.UserName, comparer).ThenBy(c => c.Target, comparer),
            CredentialSort.Type => filtered.OrderBy(c => c.TypeName, comparer).ThenBy(c => c.Target, comparer),
            CredentialSort.RecentlyModified => filtered.OrderByDescending(c => c.Modified).ThenBy(c => c.Target, comparer),
            _ => filtered.OrderBy(c => c.Target, comparer)
        }).ToArray();
    }
}
