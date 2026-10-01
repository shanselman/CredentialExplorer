param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [Parameter(Mandatory)][string]$Version,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$versionInfo = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Version $Version
Add-Type -AssemblyName System.IO.Compression
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
try {
    foreach ($name in @('AppxManifest.xml', 'CredentialExplorer.exe', 'CredentialExplorer.dll', 'resources.pri', 'Assets/AppIcon.ico')) {
        if (-not $zip.GetEntry($name)) { throw "Package is missing required file: $name" }
    }
    $reader = [System.IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Name -ne '4690860F-9384-4BBA-8C61-28BB41CBD815') { throw 'Package identity changed unexpectedly.' }
    if ($manifest.Package.Identity.Version -ne $versionInfo.PackageVersion) { throw 'MSIX version does not match the requested release.' }
    if ($manifest.Package.Identity.ProcessorArchitecture -ne $Architecture) { throw 'MSIX architecture does not match the build.' }
    if ($manifest.Package.Applications.Application.Executable -ne 'CredentialExplorer.exe') { throw 'Unexpected package executable.' }
    if ($manifest.Package.Applications.Application.EntryPoint -ne 'Windows.FullTrustApplication') { throw 'The package must retain full-trust desktop identity.' }
    $namespace = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespace.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
    if (-not $manifest.SelectSingleNode('//rescap:Capability[@Name="runFullTrust"]', $namespace)) { throw 'Full-trust capability is missing.' }
    if (@($manifest.Package.Dependencies.PackageDependency).Where({ $_.Name -like 'Microsoft.WindowsAppRuntime*' }).Count -gt 0) {
        throw 'A self-contained package must not require a separately installed Windows App Runtime.'
    }
    $binary = [System.IO.BinaryReader]::new($zip.GetEntry('CredentialExplorer.exe').Open())
    try {
        $header = $binary.ReadBytes(64)
        if ($header.Length -ne 64 -or $header[0] -ne 0x4D -or $header[1] -ne 0x5A) { throw 'Invalid executable header.' }
        $offset = [BitConverter]::ToInt32($header, 60)
        if ($offset -lt 64) { throw 'Invalid PE header offset.' }
        if ($binary.ReadBytes($offset - 64).Length -ne $offset - 64) { throw 'Truncated executable header.' }
        if ($binary.ReadUInt32() -ne 0x00004550) { throw 'Invalid PE signature.' }
        $machine = $binary.ReadUInt16()
        $expected = if ($Architecture -eq 'x64') { 0x8664 } else { 0xAA64 }
        if ($machine -ne $expected) { throw 'Executable architecture does not match the MSIX manifest.' }
    } finally { $binary.Dispose() }
    if ($RequireSignature -and -not $zip.GetEntry('AppxSignature.p7x')) { throw 'Release package is unsigned.' }
} finally { $zip.Dispose() }

if ($RequireSignature) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Package
    if ($signature.Status -ne 'Valid') { throw "Package signature verification failed: $($signature.Status)" }
}
Write-Host "Verified $Architecture MSIX layout, identity, version, and executable architecture."
