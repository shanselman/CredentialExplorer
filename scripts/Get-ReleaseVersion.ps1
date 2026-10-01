param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^v?(?<Major>0|[1-9][0-9]*)\.(?<Minor>0|[1-9][0-9]*)\.(?<Patch>0|[1-9][0-9]*)$') {
    throw 'Use a stable semantic version such as 1.2.3 or v1.2.3.'
}
$parts = @($Matches.Major, $Matches.Minor, $Matches.Patch)
foreach ($part in $parts) {
    $value = 0
    if (-not [int]::TryParse($part, [ref]$value) -or $value -gt 65535) {
        throw 'Each MSIX version component must be between 0 and 65535.'
    }
}
$normalized = $parts -join '.'
[pscustomobject]@{
    Version = $normalized
    PackageVersion = "$normalized.0"
    Tag = "v$normalized"
}
