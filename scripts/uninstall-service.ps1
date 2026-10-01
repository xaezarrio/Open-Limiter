[CmdletBinding()]
param(
    [string]$CliPath = (Join-Path $PSScriptRoot 'cli\OpenLimiter.Cli.exe'),
    [switch]$KeepPolicies
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenLimiterPolicy'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this uninstaller from an elevated PowerShell session.'
}

$programFiles = [IO.Path]::GetFullPath([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))
$installRoot = [IO.Path]::GetFullPath((Join-Path $programFiles 'OpenLimiter'))
if (-not $installRoot.StartsWith($programFiles + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved install directory escaped Program Files.'
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host 'OpenLimiter Policy Service is not installed.'
    if (Test-Path -LiteralPath $installRoot) {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
        Write-Host 'Removed the orphaned OpenLimiter Program Files directory.'
    }
    return
}

if (-not $KeepPolicies) {
    if (-not (Test-Path -LiteralPath $CliPath -PathType Leaf)) {
        throw 'The CLI is required to remove active policies. Pass -CliPath or explicitly use -KeepPolicies.'
    }

    & $CliPath clear --confirm remove-all
    if ($LASTEXITCODE -ne 0) {
        throw 'Active policies could not be removed. The service was left installed.'
    }
}

if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
}

$result = & sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
    throw "Could not delete the Windows service: $result"
}

if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}

Write-Host 'OpenLimiter Policy Service and Program Files binaries were removed. ProgramData was preserved for recovery or audit review.'
