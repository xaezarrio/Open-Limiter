[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[object]]::new()

function Add-ToolchainCheck {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    $checks.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    })
}

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudioPath = $null
$driverKitVisualStudioPath = $null
if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
    $visualStudioPath = & $vswhere `
        -latest `
        -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath

    $driverKitVisualStudioPath = & $vswhere `
        -latest `
        -products '*' `
        -requires Component.Microsoft.Windows.DriverKit.BuildTools `
        -property installationPath
}
$visualStudioDetail = if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    'Microsoft.VisualStudio.Component.VC.Tools.x86.x64 was not found.'
}
else {
    $visualStudioPath
}
Add-ToolchainCheck `
    -Name 'Visual C++ build tools' `
    -Passed (-not [string]::IsNullOrWhiteSpace($visualStudioPath)) `
    -Detail $visualStudioDetail

$kitRoot = 'C:\Program Files (x86)\Windows Kits\10'
$fwpsHeader = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'Include') -Filter 'fwpsk.h' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$ntddkHeader = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'Include') -Filter 'ntddk.h' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$fwpsDetail = if ($null -eq $fwpsHeader) { 'fwpsk.h was not found.' } else { $fwpsHeader }
$ntddkDetail = if ($null -eq $ntddkHeader) { 'ntddk.h was not found.' } else { $ntddkHeader }
Add-ToolchainCheck -Name 'WFP kernel headers' -Passed ($null -ne $fwpsHeader) -Detail $fwpsDetail
Add-ToolchainCheck -Name 'WDK kernel headers' -Passed ($null -ne $ntddkHeader) -Detail $ntddkDetail

$driverTargets = $null
if (-not [string]::IsNullOrWhiteSpace($driverKitVisualStudioPath)) {
    $driverTargets = Get-ChildItem `
        -LiteralPath (Join-Path $driverKitVisualStudioPath 'MSBuild\Microsoft\VC') `
        -Filter 'Toolset.props' `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Where-Object FullName -Match '\\PlatformToolsets\\WindowsKernelModeDriver10\.0\\Toolset\.props$' |
        Select-Object -First 1 -ExpandProperty FullName
}
$driverTargetsDetail = if ($null -eq $driverTargets) {
    'The Visual Studio Driver Kit component or WindowsKernelModeDriver10.0 platform toolset was not found.'
}
else {
    $driverTargets
}
Add-ToolchainCheck `
    -Name 'WDK MSBuild targets' `
    -Passed ($null -ne $driverTargets) `
    -Detail $driverTargetsDetail

$signTool = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'bin') -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Match '\\x64\\signtool\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$signToolDetail = if ($null -eq $signTool) { 'x64 SignTool was not found.' } else { $signTool }
Add-ToolchainCheck -Name 'SignTool' -Passed ($null -ne $signTool) -Detail $signToolDetail

$infVerifier = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'Tools') -Filter 'infverif.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Match '\\x64\\infverif\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$infVerifierDetail = if ($null -eq $infVerifier) { 'x64 InfVerif was not found.' } else { $infVerifier }
Add-ToolchainCheck -Name 'InfVerif' -Passed ($null -ne $infVerifier) -Detail $infVerifierDetail

$inf2Cat = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'bin') -Filter 'inf2cat.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Match '\\x86\\inf2cat\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$inf2CatDetail = if ($null -eq $inf2Cat) { 'Inf2Cat was not found.' } else { $inf2Cat }
Add-ToolchainCheck -Name 'Inf2Cat' -Passed ($null -ne $inf2Cat) -Detail $inf2CatDetail

$checks | Format-Table -AutoSize
if ($checks.Passed -contains $false) {
    Write-Error 'The driver toolchain is incomplete. Install the matching Windows Driver Kit and Visual Studio driver components.'
    exit 1
}

Write-Host 'The WFP driver toolchain is ready.'
