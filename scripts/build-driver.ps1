[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'check-driver-toolchain.ps1')

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudioPath = & $vswhere `
    -latest `
    -products '*' `
    -requires Component.Microsoft.Windows.DriverKit.BuildTools `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw 'Visual Studio Build Tools with the Driver Kit component was not found.'
}

$msbuild = Join-Path $visualStudioPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    $msbuild = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'driver\OpenLimiter.Wfp\OpenLimiter.Wfp.vcxproj'
$harnessProject = Join-Path $repositoryRoot 'driver\OpenLimiter.WfpHarness\OpenLimiter.WfpHarness.vcxproj'

& $msbuild $project `
    /m `
    /restore `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /verbosity:minimal

if ($LASTEXITCODE -ne 0) {
    throw "Driver build failed with exit code $LASTEXITCODE."
}

$kitRoot = 'C:\Program Files (x86)\Windows Kits\10'
$infVerifier = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'Tools') -Filter 'infverif.exe' -Recurse |
    Where-Object FullName -Match '\\x64\\infverif\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($infVerifier)) {
    throw 'The x64 InfVerif tool was not found.'
}

$driverOutput = Join-Path $repositoryRoot "artifacts\driver\x64\$Configuration"
$stampedInf = Join-Path $driverOutput 'OpenLimiter.Wfp.inf'
& $infVerifier /w $stampedInf
if ($LASTEXITCODE -ne 0) {
    throw "INF verification failed with exit code $LASTEXITCODE."
}

$catalog = Join-Path $driverOutput 'OpenLimiter.Wfp\openlimiter.wfp.cat'
if (-not (Test-Path -LiteralPath $catalog -PathType Leaf)) {
    throw "The driver catalog was not generated: $catalog"
}

& $msbuild $harnessProject `
    /m `
    /restore `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /verbosity:minimal

if ($LASTEXITCODE -ne 0) {
    throw "WFP harness build failed with exit code $LASTEXITCODE."
}
