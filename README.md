# Credential Explorer

A standalone WinUI 3 metadata-only front end over the current user's existing credential stores. There is no new password database, telemetry, network client, metadata persistence, secret reveal, copy, edit, or export.

## Features

- Separate Windows and Web / Credential Locker views with accurate per-store enumeration counts.
- Search target/resource, username, and type; sort by target, username, type, or modification time.
- Virtualized list and master-detail metadata view.
- Refresh, explicit error states, and single-entry removal with confirmation and Cancel as the default.
- System, Light, and Dark themes using native WinUI resources, including native high-contrast support.
- DPI-aware window sizing, responsive layout, accessible control names, and automation IDs.
- Isolated synthetic demo modes and a small assertion runner for safe verification.

The app currently has an English-language UI. No prebuilt installer is included; build and run locally.

## Stores and limitations

**Windows** uses `CredEnumerateW(NULL, 0)` for the current logon credential set. Each native allocation is released once with `CredFree`, including when metadata projection fails. The interop structure contains opaque blob pointers to preserve the native layout, but the app never dereferences or marshals secret contents, attributes, or comments. Removal uses the exact enumerated target and type with `CredDeleteW(..., 0)`. Extended, obsolete, and unknown types remain visible but read-only rather than guessing their deletion identity.

**Web / Credential Locker** uses the supported WinRT `PasswordVault.RetrieveAll` API. A full-trust desktop app can access the user's Credential Lockers; this is not an AppContainer app. Only Resource and UserName are projected. `RetrievePassword`, `Retrieve`, and the Password property are never used. Modification time, persistence, and owning-app identity are not supplied by this API. Removal matches exactly one Resource/UserName pair in a fresh metadata snapshot and passes that object to `PasswordVault.Remove`.

This Web view is explicitly scoped to Credential Locker, not a claim to enumerate every historical Control Panel Web Credentials vault. Undocumented `vaultcli.dll` / `VaultEnumerateItems` entry points are not used. Browser-managed databases (Edge, Chrome, Firefox, etc.) are outside scope. Errors or unsupported access appear with preserved Win32 codes/HRESULTs and redacted diagnostics, not an empty-success fallback.

Counts are per successfully enumerated store, with a separate filtered count. They are not capacity measurements. There is no universal maximum or percent-full meter. A Win32 error 8 does not prove a fixed count or byte limit. Prefixes are not treated as app ownership. Snapshots can change externally; Refresh re-enumerates, and removal errors remain explicit.

## Running locally

### Prerequisites

- Windows 10 version 1809 or newer; Windows 11 recommended.
- .NET 10 SDK.
- Visual Studio with WinUI development tools and the Windows SDK.
- [Windows App Development CLI (`winapp`)](https://github.com/microsoft/WinAppCli).
- Developer Mode already enabled for local development package registration.

Setup does not enable Developer Mode, install prerequisites, or invoke credential prompts. If using Copilot with the WinUI skills installed, `/winui-setup` can help configure missing prerequisites; invoke it yourself.

### Build and launch

From a PowerShell terminal:

```powershell
git clone https://github.com/shanselman/CredentialExplorer.git
Set-Location .\CredentialExplorer
dotnet build .\CredentialExplorer.csproj -p:Platform=x64
winapp run .\CredentialExplorer.csproj --no-build -p Platform=x64
```

On an ARM64 development machine, use `Platform=ARM64` and `RuntimeIdentifier=win-arm64` when building, and pass `-p Platform=ARM64 --arch arm64` to `winapp run`. No personal Copilot plugin paths are needed to build the project.

The normal app reads the host stores. Removal is one entry at a time, explicitly confirmed with the store, target/resource, username, type, and a warning about signing in again. Cancel is the default. There is no auto-cleanup or bulk removal. System theme is the default; Light/Dark overrides are session-only, and native high-contrast resources remain in use.

## Safe verification

```powershell
dotnet run --project .\Tests\CredentialExplorer.Tests.csproj
winapp run .\CredentialExplorer.csproj --no-build -p Platform=x64 --args "--demo"
```

`--demo` selects an isolated in-memory metadata service (163 Windows and 2 Credential Locker samples). `--demo-empty` and `--demo-failure` exercise explicit empty and failed-enumeration states. These modes never access any host credential API. The assertion runner checks native layout and secret-pointer non-access, queries, independent counts, cancellation, confirmed in-memory removal, and errors. It does not write, read, or delete real credentials. UI automation must cancel removal dialogs; it must never remove a host entry without separate specific user permission.

For a running synthetic app, `.\scripts\ui-tests.ps1 -AppPid <PID> -ArtifactDirectory <scratch-folder>` checks search, selection, cancellation, counts, sorting, virtualization, Light/Dark themes, refresh, and accessible control names/IDs. Use `-Mode Empty` or `-Mode Failure` for the corresponding launch mode. The script refuses to inspect or capture a window unless its scope notice identifies synthetic mode. Keep screenshots and test results outside the source folder.

An opt-in `dotnet run --project .\Tests\CredentialExplorer.Tests.csproj -- --native-readonly` smoke check reads host metadata without printing any names, usernames, or counts. It performs no native writes, secret reads, or removals. This is separate from the default synthetic assertion run.

## Project layout

| Path | Purpose |
| --- | --- |
| `MainPage.xaml`, `MainWindow.xaml` | WinUI application views |
| `ViewModels` | UI state, filtering, commands, and confirmation orchestration |
| `Core` | Metadata models, Windows interop, query logic, and synthetic service |
| `Services` | Supported Credential Locker integration |
| `Tests` | Synthetic assertions and opt-in redacted read-only native checks |
| `scripts` | Synthetic-only UI automation |

Do not contribute real credential metadata, host screenshots, secrets, signing keys, or local test output. Demo and test data must remain synthetic. Changes that access secret values, silently turn enumeration failures into empty stores, infer app ownership as fact, or add automatic/bulk removal are outside the current scope.

## API references

- [CredEnumerateW](https://learn.microsoft.com/windows/win32/api/wincred/nf-wincred-credenumeratew)
- [CREDENTIALW](https://learn.microsoft.com/windows/win32/api/wincred/ns-wincred-credentialw)
- [CredDeleteW](https://learn.microsoft.com/windows/win32/api/wincred/nf-wincred-creddeletew)
- [PasswordVault desktop scope](https://learn.microsoft.com/uwp/api/windows.security.credentials.passwordvault)
- [RetrieveAll](https://learn.microsoft.com/uwp/api/windows.security.credentials.passwordvault.retrieveall)
- [RetrievePassword](https://learn.microsoft.com/uwp/api/windows.security.credentials.passwordcredential.retrievepassword)
