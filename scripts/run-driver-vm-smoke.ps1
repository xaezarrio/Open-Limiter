[CmdletBinding()]
param(
    [switch]$ConfirmTestVm
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmTestVm) {
    throw 'Refusing to sign or load a kernel driver. Re-run inside a disposable test VM with -ConfirmTestVm.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window inside the test VM.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$serviceProcess = $null
$listener = $null
$cleanupDriver = $false
$runDirectory = Join-Path $repositoryRoot "artifacts\driver\vm-smoke-$PID"
$serviceLog = Join-Path $runDirectory 'service.log'
$serviceErrorLog = Join-Path $runDirectory 'service.error.log'

New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

try {
    & (Join-Path $PSScriptRoot 'build-driver.ps1') -Configuration Debug
    dotnet build (Join-Path $repositoryRoot 'OpenLimiter.slnx') --configuration Debug
    if ($LASTEXITCODE -ne 0) {
        throw "The .NET Debug build failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $PSScriptRoot 'test-sign-driver.ps1') -ConfirmTestVm
    $cleanupDriver = $true
    & (Join-Path $PSScriptRoot 'install-test-driver.ps1') -ConfirmTestVm

    $serviceExe = Join-Path $repositoryRoot 'src\OpenLimiter.Service\bin\Debug\net10.0-windows10.0.19041.0\OpenLimiter.Service.exe'
    $cliExe = Join-Path $repositoryRoot 'src\OpenLimiter.Cli\bin\Debug\net10.0-windows10.0.19041.0\OpenLimiter.Cli.exe'
    $serviceData = Join-Path $runDirectory 'service-data'
    New-Item -ItemType Directory -Force -Path $serviceData | Out-Null

    $serviceProcess = Start-Process `
        -FilePath $serviceExe `
        -ArgumentList "--data-directory `"$serviceData`"" `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $serviceLog `
        -RedirectStandardError $serviceErrorLog

    $serviceStatus = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        $serviceStatus = (& $cliExe service-status 2>&1) -join [Environment]::NewLine
        if ($LASTEXITCODE -eq 0) {
            break
        }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "The policy service did not become ready.`n$serviceStatus"
    }
    if ($serviceStatus -notmatch 'WFP driver API' -or $serviceStatus -notmatch 'inspection filters are active') {
        throw "The service did not activate the driver and dynamic WFP filters.`n$serviceStatus"
    }

    Write-Host $serviceStatus

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $acceptTask = $listener.AcceptTcpClientAsync()
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect([Net.IPAddress]::Loopback, $port)
        $serverConnection = $acceptTask.GetAwaiter().GetResult()
        $serverConnection.Dispose()
    }
    finally {
        $client.Dispose()
        $listener.Stop()
        $listener = $null
    }

    Start-Sleep -Milliseconds 500
    $flowOutput = (& $cliExe driver-flows 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $flowOutput -notmatch 'PID') {
        throw "No WFP established-flow event was observed after the loopback connection.`n$flowOutput"
    }

    Write-Host $flowOutput
    Write-Host "VM driver smoke test passed. Logs: $runDirectory"
}
finally {
    if ($null -ne $listener) {
        $listener.Stop()
    }
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id -Force
        $serviceProcess.WaitForExit()
    }
    if ($cleanupDriver) {
        try {
            & (Join-Path $PSScriptRoot 'uninstall-test-driver.ps1') -ConfirmTestVm
        }
        catch {
            Write-Warning "Automatic driver cleanup failed: $($_.Exception.Message)"
        }
    }
}
