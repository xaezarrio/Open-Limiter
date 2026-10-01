[CmdletBinding()]
param(
    [switch]$ConfirmTestVm
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenLimiterWfp'

if (-not $ConfirmTestVm) {
    throw 'Refusing to remove a kernel driver. Re-run inside the disposable test VM with -ConfirmTestVm.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window inside the test VM.'
}

$driverDirectory = [IO.Path]::GetFullPath((Join-Path $env:windir 'System32\drivers'))
$installedDriver = [IO.Path]::GetFullPath((Join-Path $driverDirectory 'OpenLimiter.Wfp.sys'))
if (-not [IO.Path]::GetDirectoryName($installedDriver).Equals($driverDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved driver target escaped the Windows drivers directory.'
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    & sc.exe stop $serviceName
    & sc.exe delete $serviceName
    if ($LASTEXITCODE -ne 0) {
        throw "Could not delete the driver service. sc.exe returned $LASTEXITCODE."
    }

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            break
        }

        Start-Sleep -Milliseconds 250
    }
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'The driver service is still present. Reboot the VM before removing its binary.'
}

if (Test-Path -LiteralPath $installedDriver -PathType Leaf) {
    Remove-Item -LiteralPath $installedDriver -Force
}

$certificateSubject = 'CN=OpenLimiter WFP Test Driver'
foreach ($store in @('Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher')) {
    Get-ChildItem -LiteralPath $store |
        Where-Object { $_.Subject -eq $certificateSubject -and $_.Issuer -eq $certificateSubject } |
        Remove-Item -Force
}

Write-Host 'The OpenLimiter WFP test driver service, binary, and test certificates were removed.'
