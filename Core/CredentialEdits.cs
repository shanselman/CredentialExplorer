namespace CredentialExplorer.Core;

public static class CredentialEdits
{
    public const int MaximumUserNameLength = 513;

    public static string? GetValidationError(CredentialMetadata entry, string? userName)
    {
        if (!entry.CanEditUserName) return "This credential type does not support username-only editing.";
        if (userName is null || userName.Contains('\0')) return "The username cannot contain a null character.";
        if (userName.Length > MaximumUserNameLength) return $"Use at most {MaximumUserNameLength} characters.";
        if (entry.NativeType == 2 && string.IsNullOrWhiteSpace(userName)) return "A domain password entry requires a username.";
        return null;
    }

    public static void Validate(CredentialMetadata entry, string userName)
    {
        if (GetValidationError(entry, userName) is not null)
            throw new CredentialStoreException("Username update: unsupported type or invalid username", 87);
    }

    public static void EnsureUnchanged(CredentialMetadata expected, CredentialMetadata current)
    {
        if (!current.HasSameIdentity(expected) ||
            !string.Equals(current.UserName, expected.UserName, StringComparison.Ordinal) ||
            current.Modified != expected.Modified || current.Persistence != expected.Persistence)
            throw new CredentialStoreException("Username update: entry changed; refresh before editing", 1306);
    }
}
