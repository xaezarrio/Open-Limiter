[CmdletBinding()]
param(
    [string]$DriverPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\driver\x64\Debug\OpenLimiter.Wfp.sys'),
    [switch]$ConfirmTestVm
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenLimiterWfp'

if (-not $ConfirmTestVm) {
    throw 'Refusing to install a kernel driver. Re-run inside a disposable test VM with -ConfirmTestVm.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window inside the test VM.'
}

$resolvedDriver = (Resolve-Path -LiteralPath $DriverPath -ErrorAction Stop).Path
$signature = Get-AuthenticodeSignature -LiteralPath $resolvedDriver
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "The driver signature is not valid: $($signature.Status). Run test-sign-driver.ps1 first."
}

$driverDirectory = [IO.Path]::GetFullPath((Join-Path $env:windir 'System32\drivers'))
$installedDriver = [IO.Path]::GetFullPath((Join-Path $driverDirectory 'OpenLimiter.Wfp.sys'))
if (-not [IO.Path]::GetDirectoryName($installedDriver).Equals($driverDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved driver destination escaped the Windows drivers directory.'
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Driver service '$serviceName' already exists. Uninstall it before replacing the binary."
}

Copy-Item -LiteralPath $resolvedDriver -Destination $installedDriver
try {
    & sc.exe create $serviceName type= kernel start= demand error= normal binPath= $installedDriver DisplayName= 'OpenLimiter WFP Test Driver'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the driver service. sc.exe returned $LASTEXITCODE."
    }

    & sc.exe start $serviceName
    if ($LASTEXITCODE -ne 0) {
        throw "The driver did not start. sc.exe returned $LASTEXITCODE."
    }

    & sc.exe query $serviceName
}
catch {
    & sc.exe stop $serviceName 2>$null | Out-Null
    & sc.exe delete $serviceName 2>$null | Out-Null
    Remove-Item -LiteralPath $installedDriver -Force -ErrorAction SilentlyContinue
    throw
}

Write-Host 'The pass-through driver is running. Validate it with the policy-service status command.'
