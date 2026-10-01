param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [string]$Version = '0.1.0',
    [string]$Publisher,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versionInfo = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Version $Version
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\packages' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$work = Join-Path $output ("work\" + $Architecture + "-" + [guid]::NewGuid().ToString('N'))
$layout = Join-Path $work 'layout'
New-Item -ItemType Directory -Force -Path $layout | Out-Null
$arguments = @(
    'publish', (Join-Path $root 'CredentialExplorer.csproj'),
    '-c', 'Release', '-r', "win-$Architecture", '--self-contained', 'true',
    "-p:Platform=$Architecture", '-p:WindowsAppSDKSelfContained=true',
    '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
    '-p:AppxPackageSigningEnabled=false', '-p:EnableWinAppRunSupport=false',
    "-p:Version=$($versionInfo.Version)", "-p:AssemblyVersion=$($versionInfo.PackageVersion)",
    "-p:FileVersion=$($versionInfo.PackageVersion)", '-p:DebugType=None', '-p:DebugSymbols=false',
    '-o', $layout, '--verbosity', 'minimal'
)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "$Architecture Release publish failed." }

[xml]$manifest = Get-Content (Join-Path $root 'Package.appxmanifest') -Raw
$manifest.Package.Identity.SetAttribute('Version', $versionInfo.PackageVersion)
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Architecture)
if ($Publisher) { $manifest.Package.Identity.SetAttribute('Publisher', $Publisher) }
$manifest.Package.Applications.Application.SetAttribute('Executable', 'CredentialExplorer.exe')
$manifest.Package.Applications.Application.SetAttribute('EntryPoint', 'Windows.FullTrustApplication')
$manifestPath = Join-Path $work 'AppxManifest.xml'
$manifest.Save($manifestPath)
$packagePath = Join-Path $output "CredentialExplorer-$($versionInfo.Tag)-win-$Architecture.msix"
& winapp package $layout --manifest $manifestPath --exe CredentialExplorer.exe --output $packagePath --skip-pri
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) { throw "$Architecture MSIX packaging failed." }
& (Join-Path $PSScriptRoot 'Test-Package.ps1') -Package $packagePath -Architecture $Architecture -Version $versionInfo.Version
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($packagePath))" | Set-Content "$packagePath.sha256" -Encoding ascii
Write-Host "Created unsigned self-contained $Architecture package: $([System.IO.Path]::GetFileName($packagePath))"
