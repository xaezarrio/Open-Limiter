[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
    $Version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Installer version '$Version' must use major.minor.patch format."
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version -Runtime win-x64 -SelfContained

$publishRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\publish\OpenLimiter-$Version-win-x64"))
$generatedPayload = Join-Path $repositoryRoot 'installer\GeneratedFiles.wxs'
& (Join-Path $PSScriptRoot 'generate-wix-payload.ps1') `
    -PublishRoot $publishRoot `
    -OutputPath $generatedPayload
$installerProject = Join-Path $repositoryRoot 'installer\OpenLimiter.Installer.wixproj'
dotnet build $installerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:PublishRoot=$publishRoot

$msiPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\installer\OpenLimiter-$Version-win-x64.msi"))
if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw "MSI build completed without producing '$msiPath'."
}

$hash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$msiPath.sha256"
[IO.File]::WriteAllText(
    $checksumPath,
    "$hash *$([IO.Path]::GetFileName($msiPath))$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Host "MSI: $msiPath"
Write-Host "SHA-256: $hash"
