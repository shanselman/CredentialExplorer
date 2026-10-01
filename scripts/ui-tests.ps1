param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidateSet('Standard', 'Empty', 'Failure')][string]$Mode = 'Standard'
)

$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[object]]::new()
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

function Invoke-UI {
    param([string[]]$Arguments)
    $output = & winapp ui @Arguments -a $AppPid 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return $output
}

function Assert-Value {
    param([string]$Id, [string]$Value, [switch]$Contains)
    $arguments = @('wait-for', $Id, '--value', $Value, '-t', '5000')
    if ($Contains) { $arguments += '--contains' }
    Invoke-UI $arguments | Out-Null
}

function Test-UI {
    param([string]$Name, [scriptblock]$Action)
    try {
        & $Action
        $results.Add(@{ name = $Name; status = 'PASS' })
        Write-Host "PASS: $Name"
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        Write-Host "FAIL: $Name - $_"
    }
}

function Capture {
    param([string]$Name)
    $windows = (Invoke-UI @('list-windows', '--json') -join "`n") | ConvertFrom-Json
    $main = $windows | Where-Object { $_.title -like 'Credential Explorer*' } | Select-Object -First 1
    if (-not $main) { throw 'Synthetic main window not found.' }
    Invoke-UI @('screenshot', '-w', "$($main.hwnd)", '-o', (Join-Path $ArtifactDirectory "$Mode-$Name.png")) | Out-Null
}

function Select-ComboItem {
    param([string]$ComboId, [string]$ItemName)
    Invoke-UI @('invoke', $ComboId) | Out-Null
    Start-Sleep -Milliseconds 250
    $search = (Invoke-UI @('search', $ItemName, '--json') -join "`n") | ConvertFrom-Json
    $item = $search.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $ItemName } | Select-Object -First 1
    if (-not $item) { throw "No accessible ComboBox item named '$ItemName'." }
    Invoke-UI @('invoke', $item.selector) | Out-Null
}

# Fail closed before any inspection, action, or screenshot that could capture host metadata.
Assert-Value 'ScopeSummary' 'SYNTHETIC DEMO' -Contains

Test-UI 'Expected initial store state' {
    switch ($Mode) {
        'Standard' { Assert-Value 'CredentialCount' '163 shown / 163 Windows entries enumerated' }
        'Empty' { Assert-Value 'CredentialCount' '0 shown / 0 Windows entries enumerated'; Assert-Value 'EmptyState' 'No entries were returned' -Contains }
        'Failure' { Assert-Value 'CredentialCount' 'Count unavailable' -Contains; Assert-Value 'ErrorText' 'Win32 5' -Contains }
    }
}
Capture 'initial'

if ($Mode -eq 'Standard') {
    Test-UI 'Scope details live in a native flyout' {
        Invoke-UI @('invoke', 'AboutStore') | Out-Null
        Assert-Value 'ScopeText' 'Counts describe enumerated entries, not capacity' -Contains
        Capture 'scope'
        Invoke-UI @('invoke', 'CloseAboutStore') | Out-Null
    }
    Test-UI 'Immediate search binding and filtered count' {
        Invoke-UI @('set-value', 'CredentialSearch', 'calendar') | Out-Null
        Assert-Value 'CredentialCount' '1 shown / 163 Windows entries enumerated'
    }
    Test-UI 'Selection supplies master-detail metadata' {
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        Assert-Value 'DetailTarget' 'Example.Calendar/demo'
        Assert-Value 'DetailUserName' 'demo-user'
        Assert-Value 'DetailType' 'Generic'
    }
    Capture 'detail'
    Test-UI 'Username editor defaults to cancel and disables unchanged saves' {
        Invoke-UI @('invoke', 'EditUserName') | Out-Null
        Assert-Value 'EditUserNameInput' 'demo-user'
        Invoke-UI @('wait-for', 'Save username', '-p', 'IsEnabled', '--value', 'False') | Out-Null
        Capture 'edit'
        Invoke-UI @('set-value', 'EditUserNameInput', 'discarded-demo') | Out-Null
        Invoke-UI @('invoke', 'Cancel') | Out-Null
        Assert-Value 'DetailUserName' 'demo-user'
        Assert-Value 'StatusText' 'Editing canceled. No credential was changed.'
    }
    Test-UI 'Confirmed edit changes only synthetic username metadata' {
        Invoke-UI @('invoke', 'EditUserName') | Out-Null
        Assert-Value 'EditDemoNotice' 'in-memory metadata only' -Contains
        Invoke-UI @('set-value', 'EditUserNameInput', 'edited-demo') | Out-Null
        Invoke-UI @('invoke', 'Save username') | Out-Null
        Assert-Value 'DetailUserName' 'edited-demo'
        Assert-Value 'DetailTarget' 'Example.Calendar/demo'
        Assert-Value 'DetailType' 'Generic'
        Assert-Value 'CredentialCount' '1 shown / 163 Windows entries enumerated'
        Assert-Value 'StatusText' 'Username updated. The existing secret was preserved.'
    }
    Test-UI 'Domain username validation is visible and blocks saving' {
        Invoke-UI @('set-value', 'CredentialSearch', 'Example.Files') | Out-Null
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        Invoke-UI @('invoke', 'EditUserName') | Out-Null
        Invoke-UI @('set-value', 'EditUserNameInput', '') | Out-Null
        Assert-Value 'EditValidationError' 'A domain password entry requires a username.'
        Invoke-UI @('wait-for', 'Save username', '-p', 'IsEnabled', '--value', 'False') | Out-Null
        Invoke-UI @('invoke', 'Cancel') | Out-Null
    }
    Test-UI 'Extended entries are read-only for username edits' {
        Invoke-UI @('set-value', 'CredentialSearch', 'Example.Extended') | Out-Null
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        Invoke-UI @('wait-for', 'EditUserName', '-p', 'IsEnabled', '--value', 'False') | Out-Null
        Invoke-UI @('set-value', 'CredentialSearch', 'calendar') | Out-Null
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
    }
    Test-UI 'Native row context menu selects the clicked entry' {
        try {
            Invoke-UI @('set-value', 'CredentialSearch', 'Example.') | Out-Null
            Invoke-UI @('click', 'CredentialRow2', '--right') | Out-Null
            Invoke-UI @('wait-for', 'ContextEditUserName', '-t', '3000') | Out-Null
            Capture 'context-menu'
            Invoke-UI @('invoke', 'ContextEditUserName') | Out-Null
            Assert-Value 'EditTarget' 'Example.Files/demo'
            Assert-Value 'EditUserNameInput' 'sample-account'
            Invoke-UI @('invoke', 'Cancel') | Out-Null
        } finally {
            Invoke-UI @('set-value', 'CredentialSearch', 'calendar') | Out-Null
            Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        }
    }
    Test-UI 'Removal confirmation defaults to cancel and names the exact entry' {
        Invoke-UI @('invoke', 'RemoveCredential') | Out-Null
        Invoke-UI @('wait-for', 'Example.Calendar/demo', '--value', 'Example.Calendar/demo', '--contains') | Out-Null
        Capture 'confirmation'
        Invoke-UI @('invoke', 'Cancel') | Out-Null
        Assert-Value 'StatusText' 'Removal canceled. No credential was changed.'
        Assert-Value 'CredentialCount' '1 shown / 163 Windows entries enumerated'
    }
    Test-UI 'No-match state never reports a zero store count' {
        Invoke-UI @('set-value', 'CredentialSearch', 'no-match-synthetic') | Out-Null
        Assert-Value 'CredentialCount' '0 shown / 163 Windows entries enumerated'
        Assert-Value 'EmptyState' 'No metadata matches your search.'
    }
    Capture 'no-match'
    Test-UI 'Separate Web counts and honest scope' {
        Invoke-UI @('invoke', 'WebStore') | Out-Null
        Assert-Value 'CredentialCount' '2 shown / 2 Credential Locker entries enumerated'
        Assert-Value 'ScopeSummary' 'not browser password databases' -Contains
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        Assert-Value 'DetailModified' 'Not supplied by this API'
        Invoke-UI @('wait-for', 'EditUserName', '-p', 'IsEnabled', '--value', 'False') | Out-Null
    }
    Capture 'web'
    Test-UI 'Refresh preserves the exact selected identity' {
        Invoke-UI @('invoke', 'RefreshCredentials') | Out-Null
        Assert-Value 'CredentialCount' '2 shown / 2 Credential Locker entries enumerated'
        Assert-Value 'DetailTarget' 'Example Locker App'
    }
    Test-UI 'Descending sort' {
        Invoke-UI @('invoke', 'WindowsStore') | Out-Null
        Select-ComboItem 'CredentialSort' 'Target: Z to A'
        Assert-Value 'CredentialSort' 'Target: Z to A'
        Invoke-UI @('invoke', 'CredentialRow0') | Out-Null
        Assert-Value 'DetailTarget' 'Synthetic.Service/159'
    }
    Test-UI 'Native list virtualization' {
        $tree = (Invoke-UI @('inspect', '--interactive', '--json') -join "`n") | ConvertFrom-Json
        $rows = @($tree.windows | ForEach-Object { $_.elements } | Where-Object { $_.automationId -like 'CredentialRow*' } | Sort-Object selector -Unique)
        if ($rows.Count -eq 0 -or $rows.Count -ge 163) { throw "Expected a realized subset of 163 rows, found $($rows.Count)." }
    }
    Test-UI 'Compact density reduces realized row height' {
        $tree = (Invoke-UI @('inspect', '--interactive', '--json') -join "`n") | ConvertFrom-Json
        $before = ($tree.windows | ForEach-Object { $_.elements } | Where-Object automationId -eq 'CredentialRow0' | Select-Object -First 1).height
        Invoke-UI @('invoke', 'AppearanceOptions') | Out-Null
        Select-ComboItem 'DensityPicker' 'Compact'
        Invoke-UI @('invoke', 'CloseAppearance') | Out-Null
        Start-Sleep -Milliseconds 250
        $tree = (Invoke-UI @('inspect', '--interactive', '--json') -join "`n") | ConvertFrom-Json
        $after = ($tree.windows | ForEach-Object { $_.elements } | Where-Object automationId -eq 'CredentialRow0' | Select-Object -First 1).height
        if ($before -le 0 -or $after -le 0 -or $after -ge $before) { throw "Compact row height $after is not smaller than comfortable $before." }
    }
    Capture 'compact'
    Test-UI 'Light theme' {
        Invoke-UI @('invoke', 'AppearanceOptions') | Out-Null
        Select-ComboItem 'ThemePicker' 'Light'
        Assert-Value 'ThemePicker' 'Light'
        Invoke-UI @('invoke', 'CloseAppearance') | Out-Null
    }
    Start-Sleep -Milliseconds 250
    Capture 'light'
    Test-UI 'Dark theme' {
        Invoke-UI @('invoke', 'AppearanceOptions') | Out-Null
        Select-ComboItem 'ThemePicker' 'Dark'
        Assert-Value 'ThemePicker' 'Dark'
        Select-ComboItem 'DensityPicker' 'Comfortable'
        Invoke-UI @('invoke', 'CloseAppearance') | Out-Null
    }
    Start-Sleep -Milliseconds 250
    Capture 'dark'
}

Test-UI 'Refresh remains usable' {
    Invoke-UI @('invoke', 'RefreshCredentials') | Out-Null
    Invoke-UI @('wait-for', 'RefreshCredentials', '-p', 'IsEnabled', '--value', 'True') | Out-Null
}
Test-UI 'Accessible names and IDs on app-owned interactive controls' {
    $tree = (Invoke-UI @('inspect', '--interactive', '--json') -join "`n") | ConvertFrom-Json
    $elements = @($tree.windows | ForEach-Object { $_.elements } | Where-Object {
        $_.className -match 'NavigationViewItem|^Button$|^AppBarButton$|^HyperlinkButton$|^ComboBox$|^TextBox$|^ListViewItem$'
    })
    $missing = @($elements | Where-Object { -not $_.automationId -or -not $_.name })
    if ($missing.Count -gt 0) { throw "$($missing.Count) app controls lack names or IDs." }
}

$results | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory "$Mode-results.json")
$failed = @($results | Where-Object status -eq 'FAIL')
Write-Host "Passed: $($results.Count - $failed.Count) | Failed: $($failed.Count)"
if ($failed.Count -gt 0) { exit 1 }
